using System.Reflection;
using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// 地点のハザード値を読む。バニラは色でしか見せないので、
    /// 数値にすること自体が本機能の付加価値になる。
    ///
    /// IL 実測（Task 4 Step 1）でブリーフの想定が崩れた点:
    ///   ブリーフの推測: Byte DisasterManager.SampleDisasterHazardMap(Vector3, SubInfoMode)
    ///   実際の宣言:     Color DisasterManager.SampleDisasterHazardMap(Vector3 pos)
    /// バニラの SampleDisasterHazardMap 自体は SubInfoMode を引数に取らない（呼んでも
    /// 絞り込みは効かない）。戻り値も byte ではなく UI 描画用に補間済みの Color。
    /// 中身は private フィールド m_hazardAmount（256x256 の byte グリッド、z*256+x で
    /// インデックス）をバイリニア補間し、InfoProperties.m_modeProperties[29]
    /// （DisasterHazard の固定インデックス、IL の ldc.i4.s 29）の
    /// neutral/active/target 色へブレンドするだけで、数値そのものは呼び出し元に
    /// 渡ってこない。Color から数値を逆算しようとすると、その 3 色の実際の RGBA
    /// （データ駆動でシリアライズされており IL からは読めない）を知らないと一般には
    /// 不可能なので、Color 経由での近似は行わない。
    ///
    /// ただし m_hazardAmount という配列自体は単一グリッドで、サブモードごとに
    /// 分かれてはいない（Task 4 フォローアップ、IL 実測で確定）。
    /// DisasterManager.UpdateTexture が各災害 AI の GetHazardSubMode を見て、
    /// 「今 InfoManager が表示しているサブモード」のハザードだけをこの配列へ
    /// 書き込む。つまりグリッドの中身は常に「現在表示中のサブモード 1 種類分」
    /// であり、SampleAt の subMode 引数はバニラ API に渡すためのものではなく、
    /// 「今表示中のサブモードと一致しているか」を確認するガードとして使う
    /// （下記 SampleAt の doc、および InfoModeSwitch.IsShowingHazardFor 参照）。
    /// これを怠ると、表示中と違う災害種別の値を要求したラベルで返す
    /// 「確信を持って誤った数値」になる。
    ///
    /// そこで本クラスは m_hazardAmount を直接読む。private フィールドへの反射アクセスは
    /// この MOD で初めてではなく、ToolRegistration.Register&lt;T&gt;() と同じ手口
    /// （フィールドが見つからなければ例外にせず警告してゼロを返す＝ゲーム更新でフィールド名が
    /// 変わっても静かに壊れるだけで落ちない）。
    ///
    /// スレッド安全性（**全体レビューで論拠を訂正**。結論は変わらないが、以前ここに
    /// 書いていた「sim スレッドが書き、main スレッドが読む」という説明は逆で誤りだった。
    /// 誤ったスレッドモデルを②以降が引き継がないよう書き直す）:
    ///
    /// m_hazardAmount に**書き込む**のは DisasterManager.UpdateTexture ただ 1 つで、
    /// これは自分でグリッドを全ゼロに埋めてから各災害 AI の UpdateHazardMap を呼ぶ
    /// （IL 実測: 先頭 IL_0000-IL_003D が 256x256 の stelem.i1 による二重ループ）。
    /// その UpdateTexture へ到達する経路は IL 全走査の結果 2 本しかなく、
    ///   - DisasterManager.LateUpdate
    ///   - DisasterManager.UpdateHazardMapping ← set_HazardMapVisible / Awake
    /// のいずれも **main スレッド**である（LateUpdate は Unity のメッセージ、
    /// set_HazardMapVisible は情報ビュー切替＝UI 経路）。加えて UpdateTexture は
    /// Texture2D を更新するので、Unity の API 制約からも main スレッド以外では動けない。
    ///
    /// つまり書き手も読み手も main スレッドであり、本クラスを main スレッドから
    /// 直接呼ぶ限り競合は起きない（WeatherReader のような sim スレッド経由・
    /// スナップショット越しの読み取りは不要）。逆に言えば、**本クラスを sim スレッドから
    /// 呼んではいけない**。そちらが新たに競合を持ち込む側になる。
    ///
    /// なお SampleDisasterHazardMap（バニラの公開 API）の IL 本体は m_hazardAmount に
    /// 対して ldfld と ldelem.u1 のみで stfld が無く、読み取り専用であることも確認済み。
    /// </summary>
    public static class HazardMapReader
    {
        // DisasterManager.m_hazardAmount: private byte[]、256x256 グリッド（z*256+x でインデックス）。
        // IL 実測: DisasterManager.Awake() で確保、SampleDisasterHazardMap / UpdateTexture が読むだけ。
        private static readonly FieldInfo HazardAmountField =
            typeof(DisasterManager).GetField("m_hazardAmount",
                BindingFlags.NonPublic | BindingFlags.Instance);

        // フィールドが見つからない場合の警告は起動あたり 1 回に抑える
        // （呼び出し頻度が高いので Log.Warn を毎回出すとログが溢れる）。
        //
        // レベルアンロードでリセットしないのは意図的。「見つからない」はこの DLL が
        // 参照しているゲームのビルドに対する事実であり、都市ごとの状態ではない
        // （プロセス寿命＝ゲーム起動から終了まで変わらない）。「セッション状態は
        // アンロードでリセットする」という本プロジェクトの原則をここに機械的に
        // 当てはめて Reset() を足すと、都市を切り替えるたびに同じ 1 行が再び
        // 出るだけの「リセットのためのリセット」になってしまう。
        private static bool _missingFieldWarned;

        // SampleAt() 内の想定外例外（GetValue の型不一致等）も同じ理由で 1 回だけ
        // Log.Error で鳴らし、以後は Log.Diag（キー単位で 512 sim フレームに 1 回）へ
        // 落とす。ここは毎フレーム呼ばれ得るパスなので、Log.Error を無条件で
        // 置いたままだと、1 回きりの取りこぼしではなく恒常的な例外（将来ゲーム更新で
        // m_hazardAmount の型が変わり GetValue が InvalidCastException を出し続ける、等）
        // が起きたときにログが埋まる。1 回目は確実に目立たせつつ、そのあとは
        // 完全に黙らせない程度に絞る。
        private static bool _sampleErrorLogged;

        // グリッドの解像度・セルサイズはバニラの public const を参照する
        // （IL 実測: HAZARDMAP_RESOLUTION=256 Int32, HAZARDMAP_CELL_SIZE=38.4 Single、
        // 両方 public static literal）。原点（グリッド中心）は解像度の半分として導出する
        // （第三の数字を別途持たない）。
        //
        // **訂正（全体レビュー指摘）**: ここには以前「参照先の const を拾うので
        // 手でコピーした数字が古くなる事故を避けられる」と書いてあったが、これは誤り。
        // C# の const（IL の literal）は**コンパイル時に呼び出し側へ焼き込まれる**ので、
        // 出荷済みの MOD DLL の中では 256 と 38.4 という即値になっている。ゲーム側が
        // 値を変えても、この MOD を再ビルドしない限り追従しない——つまり手でコピーした
        // 数字を持つのと実行時の安全性は同じである。
        // 再ビルドを挟まずに食い違いを検知できる唯一の方法は、**実行時にロード中の
        // ゲームのメタデータを読む**ことなので、その検査を Assumptions 側へ足した
        // （"DisasterManager hazard grid geometry is 256 x 38.4"）。
        private const int GridSize = DisasterManager.HAZARDMAP_RESOLUTION;
        private const float WorldUnitsPerCell = DisasterManager.HAZARDMAP_CELL_SIZE;
        private const float GridOrigin = DisasterManager.HAZARDMAP_RESOLUTION / 2f;

        /// <summary>
        /// worldPos 地点のハザード強度を 0-255 で返す。
        ///
        /// ok=false になるのは 3 通り: DisasterManager が居ない／要求した subMode が
        /// 表示中でない（下記）／worldPos がグリッド（±4915.2 m）の外。
        /// いずれも「読めなかった」であって「0 だった」ではない。
        ///
        /// **ok=true でも値が意味を持つとは限らない点に注意。** グリッドは
        /// 「測位済みかつ進行中の嵐」が 1 つも無ければ全セル 0 になる
        /// （ForecastPanel.RefreshCursorHazard の doc 参照）。その 0 を
        /// 「ここは安全」と読ませないための判定は本クラスの責務ではなく、
        /// WeatherSnapshot の測位済み件数を見る呼び出し側の責務。
        ///
        /// subMode は「これから読みたいハザード種別」の指定であり、単なる形だけの引数
        /// ではない。m_hazardAmount は単一グリッドで、今 InfoManager が表示している
        /// サブモードのハザードしか保持していない（クラス doc 参照）ため、subMode が
        /// 現在表示中のサブモードと一致しているかを
        /// <see cref="InfoModeSwitch.IsShowingHazardFor"/> で確認し、一致しなければ
        /// グリッドの値を返さず ok=false にする。ここで弾かないと、呼び出し元が
        /// 気付かないまま無関係な災害種別の数値に要求したラベルを付けて表示して
        /// しまう（この MOD が避けたい「確信を持って誤った数値」そのもの）。
        ///
        /// 格子の左下（floor）側 1 セルだけを読む点に注意。バニラの
        /// SampleDisasterHazardMap は 4 隅をバイリニア補間した Color を返すが、
        /// こちらは補間せず生の格子値を返す（Color 経由の逆算は不正確になるため、
        /// 上のクラス doc の通り意図して採用していない）。そのため、セルの境界付近では
        /// この戻り値とバニラのヒートマップの見た目が最大 1 セル分ずれ得る。
        /// これはバグではなく「不正確な色からの逆算より、生の格子値の方がマシ」という
        /// トレードオフの結果。
        /// </summary>
        public static byte SampleAt(Vector3 worldPos, InfoManager.SubInfoMode subMode, out bool ok)
        {
            ok = false;
            try
            {
                if (!Singleton<DisasterManager>.exists) return 0;

                // グリッドは「今表示中のサブモード」の値しか保持していない
                // （クラス doc 参照）。要求した subMode が表示中でなければ、
                // グリッドの値は無関係な災害種別のものなので「読み取れなかった」
                // として扱う。ラベルと数値が食い違う「確信を持って誤った数値」を
                // 呼び出し元に渡さないための必須ガード。
                if (!InfoModeSwitch.IsShowingHazardFor(subMode)) return 0;

                if (HazardAmountField == null)
                {
                    if (!_missingFieldWarned)
                    {
                        _missingFieldWarned = true;
                        Log.Error("DisasterManager.m_hazardAmount field not found (game update?)", null);
                    }
                    return 0;
                }

                var map = (byte[])HazardAmountField.GetValue(Singleton<DisasterManager>.instance);
                if (map == null || map.Length == 0) return 0;

                int gx = Mathf.FloorToInt(worldPos.x / WorldUnitsPerCell + GridOrigin);
                int gz = Mathf.FloorToInt(worldPos.z / WorldUnitsPerCell + GridOrigin);

                // グリッドの外は「読めなかった」として返す。以前はここで Clamp して
                // いたが、それは端のセルの値を「カーソル位置のハザード」として
                // 返すことになり、この機能が避けたい「確信を持って誤った数値」に当たる
                // （全体レビュー指摘）。グリッドが覆うのは 256 * 38.4 / 2 = ±4915.2 m で、
                // 81 タイル系の MOD を入れるとカーソルは普通にこの外へ出る。
                // そこで返る値は「そこのハザード」ではなく「一番近い端のハザード」であり、
                // ラベルだけが正しくて中身が別地点、という最も見抜きにくい形の嘘になる。
                if (gx < 0 || gx >= GridSize || gz < 0 || gz >= GridSize) return 0;

                int index = gz * GridSize + gx;
                if (index < 0 || index >= map.Length) return 0;

                ok = true;
                return map[index];
            }
            catch (System.Exception e)
            {
                // SampleAt はカーソルが地図上にある間ずっと毎フレーム呼ばれ得るパス。
                // 1 回目だけ確実に目立たせ（Log.Error）、以後は Log.Diag のキー単位
                // スロットル（512 sim フレームに 1 回）に落として流量を抑える
                // （完全に黙らせるのではなく、間隔を空けて出し続ける）。
                if (!_sampleErrorLogged)
                {
                    _sampleErrorLogged = true;
                    Log.Error("hazard sample failed", e);
                }
                else
                {
                    Log.Diag("HazardSample", "hazard sample failed: " + e.GetType().Name);
                }
                return 0;
            }
        }
    }
}
