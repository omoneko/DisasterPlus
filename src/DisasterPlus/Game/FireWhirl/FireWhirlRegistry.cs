using System.Collections.Generic;
using DisasterPlus.Core.Common;
using DisasterPlus.Core.FireWhirl;

namespace DisasterPlus.Game
{
    /// <summary>生存中の火災旋風 1 基。レジストリの内部表現なので外に漏らさない。</summary>
    internal class ActiveFireWhirl
    {
        public ushort DisasterId;
        public ushort VehicleId;
        public Vec3 Center;
        public float Radius;
        public int BurningCount;
        public FireWhirlLifecycle Life;

        /// <summary>終了処理に入った。移動目標を現在地に置いてバニラの解体を待っている状態。</summary>
        public bool Ending;

        /// <summary>
        /// 終了処理に入ってからのゲーム内経過（分）。
        ///
        /// Life.ElapsedMinutes では代用できない。FireWhirlFeature.UpdateExisting は
        /// Ending の旋風を飛ばすので、Ending が立った瞬間に Life の時計は止まる。
        /// 「終了処理に入ったのに終わらない」（＝m_targetPos0 取り違えの再発シグネチャ）
        /// を測るには、Ending 専用の別の時計が要る。
        /// </summary>
        public float EndingMinutes;

        /// <summary>
        /// プレイヤーが災害パネルから手動で置いた旋風。
        /// 発生条件（R 内に N 棟）の割り込み判定を免除し、絶対上限だけで終わらせる。
        /// これが無いと、火の無い場所に置いた瞬間に条件割り込みが始まり、
        /// 猶予（既定 3 分 ≒ 136 フレーム）で消えてしまい、動作確認に使えない。
        /// </summary>
        public bool Manual;
    }

    /// <summary>
    /// レジストリの外へ渡す不変のコピー。
    ///
    /// 可変オブジェクトの参照を返すと、リストを複製しても中身は共有されたままで、
    /// main スレッドがロックの外で Center や Ending を読んでいる最中に
    /// sim スレッドが書き換えられてしまう。値でコピーして初めて境界が成立する。
    /// </summary>
    public struct FireWhirlView
    {
        public readonly ushort DisasterId;
        public readonly ushort VehicleId;
        public readonly Vec3 Center;
        public readonly float Radius;
        public readonly int BurningCount;
        public readonly float ElapsedMinutes;
        public readonly bool Ending;

        /// <summary>終了処理に入ってからのゲーム内経過（分）。Ending でなければ 0。</summary>
        public readonly float EndingMinutes;

        public readonly bool Manual;

        internal FireWhirlView(ActiveFireWhirl w)
        {
            DisasterId = w.DisasterId;
            VehicleId = w.VehicleId;
            Center = w.Center;
            Radius = w.Radius;
            BurningCount = w.BurningCount;
            ElapsedMinutes = w.Life.ElapsedMinutes;
            Ending = w.Ending;
            EndingMinutes = w.EndingMinutes;
            Manual = w.Manual;
        }
    }

    /// <summary>
    /// この MOD で唯一の共有可変状態。
    /// sim スレッド（判定・生成・被害）と main スレッド（描画）と
    /// Harmony パッチ（位置固定）の 3 者から触られる。
    ///
    /// net35 に System.Collections.Concurrent は無いので、素の lock で snapshot-then-render する。
    /// </summary>
    public static class FireWhirlRegistry
    {
        /// <summary>旋風が消えた地点。この間は同じ場所に再発生させない。</summary>
        private struct CoolingSpot
        {
            public Vec2 Center;
            public float RemainingMinutes;
        }

        private static readonly object _gate = new object();
        private static readonly List<ActiveFireWhirl> _active = new List<ActiveFireWhirl>();
        private static readonly List<CoolingSpot> _cooling = new List<CoolingSpot>();

        /// <param name="manual">プレイヤーが手動で置いたか。条件割り込みの免除に使う。</param>
        public static void Add(ushort disasterId, ushort vehicleId, Vec3 center, float radius,
                               int burningCount, bool manual)
        {
            lock (_gate)
            {
                _active.Add(new ActiveFireWhirl
                {
                    DisasterId = disasterId,
                    VehicleId = vehicleId,
                    Center = center,
                    Radius = radius,
                    BurningCount = burningCount,
                    Life = FireWhirlLifecycle.Start(),
                    Ending = false,
                    EndingMinutes = 0f,
                    Manual = manual,
                });
            }
        }

        /// <param name="cooldownMinutes">
        /// この地点を抑制し続ける時間。仕様では最大持続時間と同値
        /// （呼び出し側が ModSettings.MaxLifetimeMinutes を渡す）。
        /// </param>
        public static void Remove(ushort disasterId, float cooldownMinutes)
        {
            lock (_gate)
            {
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    if (_active[i].DisasterId != disasterId) continue;

                    if (cooldownMinutes > 0f)
                    {
                        _cooling.Add(new CoolingSpot
                        {
                            Center = _active[i].Center.ToVec2(),
                            RemainingMinutes = cooldownMinutes,
                        });
                    }
                    _active.RemoveAt(i);
                }
            }
        }

        public static int Count
        {
            get { lock (_gate) { return _active.Count; } }
        }

        /// <summary>
        /// クールダウン中（＝再発生が抑制されている）の地点数。
        ///
        /// 設計書 7.1 のオーバーレイ例は末尾に cooldown を出しており、
        /// 「大火災が燃えているのに旋風が出ない」の最有力の原因がこれなので、
        /// 外から読めないままにしない。
        /// </summary>
        public static int CoolingCount
        {
            get { lock (_gate) { return _cooling.Count; } }
        }

        /// <summary>
        /// 値でコピーした不変ビューを返す。呼び出し側はロックの外で自由に走査してよい。
        /// ActiveFireWhirl の参照は決して外に出さない。
        /// </summary>
        public static List<FireWhirlView> Snapshot()
        {
            lock (_gate)
            {
                var list = new List<FireWhirlView>(_active.Count);
                for (int i = 0; i < _active.Count; i++) list.Add(new FireWhirlView(_active[i]));
                return list;
            }
        }

        /// <summary>寿命を進める。sim スレッドから呼ぶ。</summary>
        public static void AdvanceLife(ushort disasterId, float deltaMinutes, bool conditionMet)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    _active[i].Life = _active[i].Life.Advance(deltaMinutes, conditionMet);
                    return;
                }
            }
        }

        /// <summary>燃焼規模の変化を反映する。sim スレッドから呼ぶ。</summary>
        public static void UpdateStrength(ushort disasterId, float radius, int burningCount)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    _active[i].Radius = radius;
                    _active[i].BurningCount = burningCount;
                    return;
                }
            }
        }

        /// <summary>寿命判定。Life を外に漏らさないため、評価もレジストリ内で行う。</summary>
        public static FireWhirlVerdict EvaluateVerdict(ushort disasterId, FireWhirlConfig config)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    return _active[i].Life.Evaluate(config);
                }
            }
            return FireWhirlVerdict.Dissipate;   // 見つからない = 既に消えている
        }

        /// <summary>
        /// 渦車両を紐づける。車両は ActivateDisaster が作るので、CreateDisaster 直後には
        /// まだ存在せず、Add した時点では vehicleId = 0 のまま登録されている。
        /// </summary>
        public static void SetVehicle(ushort disasterId, ushort vehicleId)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    _active[i].VehicleId = vehicleId;
                    return;
                }
            }
        }

        /// <summary>終了処理に入ったことを記録する。以降は位置固定をやめる。</summary>
        public static void MarkEnding(ushort disasterId)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].DisasterId != disasterId) continue;
                    _active[i].Ending = true;
                    _active[i].EndingMinutes = 0f;   // 終了処理の時計をここで始める
                    return;
                }
            }
        }

        /// <summary>
        /// 終了処理中の旋風の「終了してからの経過」を進める。sim スレッドから毎 tick 呼ぶ。
        /// 機能設定が OFF でも呼ぶこと（OFF にした瞬間に全基が Ending に入るため）。
        /// </summary>
        public static void AdvanceEnding(float deltaMinutes)
        {
            if (deltaMinutes <= 0f) return;
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (!_active[i].Ending) continue;
                    _active[i].EndingMinutes += deltaMinutes;
                }
            }
        }

        /// <summary>
        /// 発生を抑制すべき地点の一覧。生存中の旋風の中心と、
        /// 最近まで旋風があった地点（クールダウン中）の両方を返す。
        ///
        /// クールダウンが無いと、消えた直後に同じ大火災が同じ場所で再発生させ続けて
        /// 実質的に永久の旋風になる（絶対上限の意味が無くなる）。
        /// </summary>
        public static List<Vec2> Centers()
        {
            lock (_gate)
            {
                var list = new List<Vec2>(_active.Count + _cooling.Count);
                for (int i = 0; i < _active.Count; i++) list.Add(_active[i].Center.ToVec2());
                for (int i = 0; i < _cooling.Count; i++) list.Add(_cooling[i].Center);
                return list;
            }
        }

        /// <summary>
        /// クールダウンを進め、明けた地点を捨てる。sim スレッドから毎 tick 呼ぶ。
        /// </summary>
        public static void AdvanceCooldowns(float deltaMinutes)
        {
            if (deltaMinutes <= 0f) return;
            lock (_gate)
            {
                for (int i = _cooling.Count - 1; i >= 0; i--)
                {
                    var c = _cooling[i];
                    c.RemainingMinutes -= deltaMinutes;
                    if (c.RemainingMinutes <= 0f) _cooling.RemoveAt(i);
                    else _cooling[i] = c;
                }
            }
        }

        /// <summary>
        /// Harmony パッチ（VortexAI.SimulationStep の Postfix）から毎ステップ呼ばれる。
        /// 自分が作った渦かどうかを車両 ID で判定し、固定先の座標を返す。
        ///
        /// 終了処理中（Ending）でも固定を続ける。ここで固定を解くと、目標に到達するまでの
        /// スピンダウン（角速度 1.0 から -0.05/step で 0.05 未満まで約 20 ステップ、その後
        /// ArriveAtDestination が m_waitCounter > 4 になるまで 5 ステップ）の間、渦が
        /// 自由に動き回ってしまい、炎エフェクトだけが発生地点に取り残される。
        /// 固定したままなら目標との距離が 0 のままなので、スピンダウンが確実に進む。
        /// エントリは Unspawn 後に CollectFinished が外す。
        /// </summary>
        public static bool TryGetPinnedCenter(ushort vehicleId, out Vec3 center)
        {
            lock (_gate)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (_active[i].VehicleId != vehicleId) continue;
                    center = _active[i].Center;
                    return true;
                }
            }
            center = new Vec3(0f, 0f, 0f);
            return false;
        }

        /// <summary>レベルアンロード時。都市をまたいで状態を持ち越さない。</summary>
        public static void Clear()
        {
            lock (_gate)
            {
                _active.Clear();
                _cooling.Clear();
            }
        }

        /// <summary>
        /// セーブから復元する。現在の _active を置き換える（呼び出し前の内容は消える）。
        /// 車両 ID は 0 のままにして、FireWhirlPinner.AttachVehicles がロード後に付け直す。
        /// 経過時間は保存値から積み直すので、ロードしても寿命が延びない。
        ///
        /// 呼び出し順の注意: DisasterPlusSerialization.OnLoadData は
        /// DisasterPlusLoading.OnLevelLoaded（内部で FireWhirlRegistry.Clear() を呼ぶ）より前に
        /// 完了する。そのため OnLoadData から直接ここを呼ぶと Clear() で消される。
        /// 呼び出しは OnLevelLoaded 側で Clear() の後に行うこと
        /// （DisasterPlusSerialization.TakePendingRestore() 経由）。
        /// </summary>
        public static void RestoreFromSave(List<SavedFireWhirl> saved)
        {
            if (saved == null) return;

            lock (_gate)
            {
                _active.Clear();
                for (int i = 0; i < saved.Count; i++)
                {
                    _active.Add(new ActiveFireWhirl
                    {
                        DisasterId = saved[i].DisasterId,
                        VehicleId = 0,
                        Center = saved[i].Center,
                        Radius = saved[i].Radius,
                        BurningCount = saved[i].BurningCount,
                        Life = FireWhirlLifecycle.Start().Advance(saved[i].ElapsedMinutes, true),
                        Ending = false,
                        EndingMinutes = 0f,
                        Manual = saved[i].Manual,
                    });
                }
            }
        }
    }
}
