using ColossalFramework;
using DisasterPlus.Core.Common;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// <see cref="TyphoonTornado"/> のうち**渦車両の紐づけと操舵**。
    /// <b>sim スレッド専用</b>（本体と同じ）。
    ///
    /// 本体（<c>TyphoonTornado.cs</c>）から切り出したのは、全体レビューの修正で
    /// あちらが 910 行まで育ち、800 行の上限を越えたためである。切り目は
    /// 「台帳の寿命」と「車両の操作」——罠 3（**両スロットに書く**）と
    /// 車両 ID の再利用の扱いは全部こちら側にまとまる。
    ///
    /// **状態（<c>_slots</c> / <c>_count</c>）はここでは持たない。** partial なので
    /// 本体の private フィールドにそのまま届く（可視性は 1 つも上げていない）。
    /// </summary>
    public static partial class TyphoonTornado
    {
        // ── 紐づけ ─────────────────────────────────────────────

        /// <summary>
        /// 渦車両を探す。**災害が Active になるまでは探さない**（<see cref="MaxAttachTicks"/>）。
        /// </summary>
        private static void Attach(DisasterData[] buffer)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                ushort id = _slots[i].DisasterId;
                if (id == 0 || id >= buffer.Length || _slots[i].VehicleId != 0) continue;
                if ((buffer[id].m_flags & DisasterData.Flags.Active) == 0) continue;
                if (_slots[i].AttachTicks >= MaxAttachTicks) continue;

                _slots[i].AttachTicks++;

                ushort found = FindVortexVehicle(id, buffer[id].m_targetPosition,
                                                 buffer[id].m_intensity);
                if (found != 0)
                {
                    _slots[i].VehicleId = found;
                    Log.Info("typhoon tornado " + id + " attached to vortex vehicle " + found);
                    continue;
                }

                // **隠さない。** 諦めた竜巻は④の軌道に乗らず、バニラの竜巻として
                // 自由に流れる（見た目も破壊も出る）。診断の attached < count が示す。
                if (_slots[i].AttachTicks == MaxAttachTicks)
                {
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyTorAttach",
                             "typhoon tornado " + id + " never got a vortex vehicle; "
                             + "it drifts on vanilla's own path");
                }
            }
        }

        /// <summary>
        /// 災害グループに属する車両のうち、渦の <c>VehicleAI</c> を持つものを探す。
        /// <c>TornadoAI.GetPosition</c> と同じ経路（<c>InstanceID.Disaster</c> →
        /// <c>InstanceManager.GetAllGroupInstances</c>）。③の
        /// <c>FireWhirlPinner.FindVortexVehicle</c> を写した形で、
        /// **発生地点から離れすぎている候補は棄却する**（災害 ID 使い回し対策）。
        /// </summary>
        private static ushort FindVortexVehicle(ushort disasterId, Vector3 expectedCentre,
                                                byte intensity)
        {
            var id = InstanceID.Empty;
            id.Disaster = disasterId;

            _tempInstances.Clear();
            InstanceManager.GetAllGroupInstances(id, _tempInstances);
            if (!Singleton<VehicleManager>.exists) return 0;

            var buffer = Singleton<VehicleManager>.instance.m_vehicles.m_buffer;
            if (buffer == null) return 0;

            // TornadoAI.ActivateDisaster の生成距離（本タスクで IL 実測）。
            float expected = intensity * 10f + 400f;
            float limit = expected * AttachDistanceFactor;
            float limitSq = limit * limit;

            for (int i = 0; i < _tempInstances.m_size; i++)
            {
                ushort v = _tempInstances.m_buffer[i].Vehicle;
                if (v == 0 || v >= buffer.Length) continue;

                var info = buffer[v].Info;
                if (info == null || !(info.m_vehicleAI is VortexAI)) continue;

                Vector3 pos = buffer[v].GetLastFrameData().m_position;
                float dx = pos.x - expectedCentre.x;
                float dz = pos.z - expectedCentre.z;
                if (dx * dx + dz * dz > limitSq)
                {
                    // 遠すぎる = 災害 ID 使い回しで無関係な渦を拾った可能性。
                    // ここで掴むとバニラの竜巻を④が引きずり回すことになる。
                    Log.Diag(DisasterPlus.Core.Diagnostics.LogChannel.Typhoon, "TyTorReject",
                             "vortex vehicle " + v + " for tornado " + disasterId
                             + " is too far from the expected centre; rejecting");
                    continue;
                }

                return v;
            }
            return 0;
        }

        // ── 操舵 ───────────────────────────────────────────────

        /// <summary>
        /// 台風の中心の周りを回らせる。位相の進みには**2 段の上限**がある。
        /// まず 1 tick の角度の増分を「車両が追える距離 ÷ 軌道半径」で抑える
        /// （抑えないと位相だけが先へ進み、竜巻は円ではなく弦を横切る）。次に、実際に
        /// 書く目標点の移動量も <paramref name="maxStep"/> でクランプする ——
        /// 台風の中心自体が動くので、位相を抑えるだけでは足りない。
        /// </summary>
        private static void Steer(DisasterData[] buffer, float deltaMinutes,
                                  float radius, float maxStep)
        {
            float desiredStep = OrbitDegreesPerMinute * deltaMinutes * 0.017453292f;
            float reachableStep = maxStep / radius;
            float phaseStep = desiredStep < reachableStep ? desiredStep : reachableStep;

            byte intensity = TornadoIntensity();
            float angle = TyphoonController.HeadingRadians;

            for (int i = 0; i < _slots.Length; i++)
            {
                ushort id = _slots[i].DisasterId;
                if (id == 0 || id >= buffer.Length) continue;

                // Verify がこの tick で確かめているが、get_Info は境界検査をしない
                // 4 命令なので、参照は毎回自分で確かめてから使う。
                var info = buffer[id].Info;
                if (info == null || info.m_disasterAI == null) continue;

                _slots[i].OrbitPhase += phaseStep;

                var wanted = OrbitPoint(_slots[i].OrbitPhase, radius);
                var target = ClampStep(_slots[i].Target, wanted, maxStep);

                var ai = info.m_disasterAI;
                ai.ClampDisasterTarget(ref target);
                target.y = SampleHeight(target);

                // Emerging 中も書く。ActivateDisaster は m_targetPosition を読んで
                // 渦車両の生成位置を決めるので（§B-2）、書いておくと軌道上に生まれる。
                buffer[id].m_targetPosition = target;
                buffer[id].m_angle = angle;
                buffer[id].m_intensity = intensity;
                _slots[i].Target = target;

                ushort v = _slots[i].VehicleId;
                if (v == 0) continue;

                // ★ **車両 ID も再利用される。** 渦が Unspawn されたあと同じ添字が
                //   普通のバスに配られる。掴んだままだと④がその車両の目標地点を毎 tick
                //   書き潰し、市内の車が 1 台だけ台風の周りを回り出す（例外は出ない）。
                //   探し直しはさせない —— 渦が消えた＝竜巻自体が終わりかけなので
                //   （TornadoAI.IsStillActive は渦が生きている間だけ true。§B-2）、
                //   Verify がまもなくスロットを外す。
                if (!StillOurVortex(v))
                {
                    _slots[i].VehicleId = 0;
                    _slots[i].AttachTicks = MaxAttachTicks;
                    continue;
                }

                // ★ 罠 3: **両スロットに書く**（クラス doc）。
                WriteVehicleTarget(v, target);
            }
        }

        /// <summary>
        /// <paramref name="from"/> から <paramref name="to"/> へ、水平距離
        /// <paramref name="maxStep"/> までしか動かさない。
        /// </summary>
        private static Vector3 ClampStep(Vector3 from, Vector3 to, float maxStep)
        {
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            float d2 = dx * dx + dz * dz;
            if (d2 <= maxStep * maxStep || d2 <= 0f) return to;

            float scale = maxStep / Mathf.Sqrt(d2);
            return new Vector3(from.x + dx * scale, to.y, from.z + dz * scale);
        }

        /// <summary>位相と半径から台風の中心まわりの点を出す。</summary>
        private static Vector3 OrbitPoint(float phase, float radius)
        {
            Vec3 centre = TyphoonController.Centre;
            return new Vector3(centre.X + Mathf.Cos(phase) * radius,
                               0f,
                               centre.Z + Mathf.Sin(phase) * radius);
        }

        /// <summary>
        /// 台風の今の強度から竜巻の強度を出す。**<c>(byte)</c> へのキャストの前に
        /// クランプすること**（範囲外を先にキャストすると最弱の台風が最強の竜巻を産む）。
        /// </summary>
        private static byte TornadoIntensity()
        {
            int value = (int)(TyphoonController.Intensity * IntensityFraction);
            if (value < MinTornadoIntensity) value = MinTornadoIntensity;
            if (value > MaxTornadoIntensity) value = MaxTornadoIntensity;
            return (byte)value;
        }
    }
}
