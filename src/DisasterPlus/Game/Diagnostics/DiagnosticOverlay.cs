using System.Collections.Generic;
using DisasterPlus.Core.Diagnostics;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// IMGUI のデバッグオーバーレイ。CS の UI フレームワークには一切依存しない。
    ///
    /// OnGUI は 1 フレームに複数回呼ばれる(レイアウト用と描画用)。
    /// したがってホットキー判定と状態取得は Update で行い、OnGUI では描画だけする。
    /// </summary>
    public class DiagnosticOverlay : MonoBehaviour
    {
        private const string HostName = "DisasterPlusDiagnosticOverlay";

        private static DiagnosticOverlay _instance;

        private bool _visible;
        private List<string> _lines = new List<string>();
        private Vector2 _scroll;
        private GUIStyle _style;
        private Texture2D _background;

        public static bool Visible { get { return _instance != null && _instance._visible; } }

        public static DiagnosticOverlay Create()
        {
            // fake-null に注意: 破棄済みの MonoBehaviour は == null が true になる。
            // ここはコレクションではなくオブジェクト自体を見ているので正しく再生成される。
            if (_instance != null) return _instance;

            var go = new GameObject(HostName);
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<DiagnosticOverlay>();
            return _instance;
        }

        public static void Destroy()
        {
            if (_instance == null) return;
            Object.Destroy(_instance.gameObject);
            _instance = null;
            // 表示中だった場合に備え、収集フラグは即座に落とす。OnDestroy 経由でも
            // 落ちるが、破棄はフレーム末まで遅延することがあるため二重に安全策を取る。
            DiagnosticsHub.CollectionEnabled = false;
        }

        private void Update()
        {
            ModSettings.Ensure();

            var key = (KeyCode)ModSettings.OverlayHotkey.value;
            if (Input.GetKeyDown(key))
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (ctrl)
                {
                    // 依頼を立てるだけ。組み立ては次の sim tick、書き出しはこの下の
                    // FlushPendingWrite() が拾う。ここで直接書かない
                    // （BuildReport は sim スレッド専用の契約のため）。
                    DiagnosticDump.RequestDump();
                }
                else
                {
                    Toggle();
                }
            }

            // 収集が走るのは表示中だけ。閉じていればコストはゼロ。
            DiagnosticsHub.CollectionEnabled = _visible;

            if (_visible)
            {
                _lines = DiagnosticFormatter.Format(DiagnosticsHub.Latest);
            }

            // sim スレッドが組み立て終えたダンプがあれば、ここ(main スレッド)で書き出す。
            // オーバーレイを閉じていても(_visible が false でも)この呼び出し自体は続けるので、
            // 表示していないときの Ctrl+ホットキーでもダンプは書き出される。
            DiagnosticDump.FlushPendingWrite();
        }

        private void Toggle()
        {
            _visible = !_visible;
            if (!_visible) _lines.Clear();
            Log.Info("diagnostic overlay " + (_visible ? "shown" : "hidden"));
        }

        private void OnGUI()
        {
            if (!_visible) return;
            EnsureStyle();

            const float width = 560f;
            float height = Mathf.Min(Screen.height - 80f, 24f + _lines.Count * 16f);

            var area = new Rect(12f, 60f, width, height);
            GUI.DrawTexture(area, _background);
            GUILayout.BeginArea(area);
            GUILayout.Label("Disaster +  [" + (KeyCode)ModSettings.OverlayHotkey.value
                            + " toggle / Ctrl+ dump]", _style);

            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _lines.Count; i++)
            {
                GUILayout.Label(_lines[i], _style);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void EnsureStyle()
        {
            // fake-null 対策: 都市を跨ぐと Texture2D が破棄されている場合がある。
            // オブジェクト自体を見て作り直す。
            if (_background == null)
            {
                _background = new Texture2D(1, 1);
                _background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.78f));
                _background.Apply();
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label);
                _style.fontSize = 12;
                _style.wordWrap = false;
                _style.normal.textColor = Color.white;
                _style.padding = new RectOffset(4, 4, 0, 0);
            }
        }

        private void OnDestroy()
        {
            if (_background != null) Object.Destroy(_background);
            _background = null;
            _style = null;
            // Object.Destroy は破棄が次フレーム末まで遅延することがある。もし旧
            // インスタンスの OnDestroy が、新しい生存インスタンスが生成され既に
            // 収集を有効化した後に発火すると、this が現行の _instance でない限り
            // ここで収集を止めてはいけない（生きているオーバーレイの分まで
            // 次の Update まで無効化してしまう）。_instance の判定と同じ
            // 「古いオブジェクトの発言は無視する」規律をここにも適用する。
            if (_instance == this)
            {
                DiagnosticsHub.CollectionEnabled = false;
                _instance = null;
            }
        }
    }
}
