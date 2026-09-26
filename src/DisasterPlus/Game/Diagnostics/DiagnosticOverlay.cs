using System.Collections.Generic;
using DisasterPlus.Core.Diagnostics;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// An IMGUI debug overlay. It depends on CS's UI framework not at all.
    ///
    /// OnGUI is called several times per frame (once for layout, once for drawing).
    /// So hotkey handling and fetching the state happen in Update, and OnGUI only draws.
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
            // Mind the fake-null: == null is true for a destroyed MonoBehaviour.
            // This looks at the object itself rather than a collection, so it is correctly
            // recreated.
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
            // In case it was being displayed, drop the collection flag immediately. It also
            // drops via OnDestroy, but destruction can be deferred to the end of the frame, so
            // take the precaution twice.
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
                    // Only raise the request. Assembly happens on the next sim tick, and the
                    // write is picked up by FlushPendingWrite() below. Do not write directly
                    // here (BuildReport is contracted as sim-thread only).
                    DiagnosticDump.RequestDump();
                }
                else
                {
                    Toggle();
                }
            }

            // Collection only runs while it is displayed. Closed, the cost is zero.
            DiagnosticsHub.CollectionEnabled = _visible;

            if (_visible)
            {
                _lines = DiagnosticFormatter.Format(DiagnosticsHub.Latest);
            }

            // Writing the dump (DiagnosticDump.FlushPendingWrite) must not happen here.
            // This object itself may not exist depending on the settings, so having the write
            // ride along on this Update creates a hidden dependency: "overlay OFF means no
            // dump either". The call belongs in FeatureHost.MainThreadUpdate().
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
            // Guarding against the fake-null: the Texture2D can have been destroyed when
            // crossing between cities. Look at the object itself and rebuild.
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
            // Object.Destroy can defer the destruction to the end of the next frame. If the
            // old instance's OnDestroy fires after a new, live instance has been created and
            // has already enabled collection, we must not stop collection here unless this is
            // the current _instance (doing so would disable it on the live overlay's behalf
            // until its next Update). Apply the same "ignore what an old object says"
            // discipline as the _instance check.
            if (_instance == this)
            {
                DiagnosticsHub.CollectionEnabled = false;
                _instance = null;
            }
        }
    }
}
