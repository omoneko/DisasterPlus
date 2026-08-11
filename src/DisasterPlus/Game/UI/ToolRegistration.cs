using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// カスタム ToolBase を ToolController に認識させる。
    ///
    /// ToolController.m_tools は Awake で GetComponents&lt;ToolBase&gt;() から一度だけ構築され、
    /// ToolsModifierControl.SetTool&lt;T&gt; は静的辞書を TryGetValue するだけ。
    /// どちらも起動後に足したツールを知らないので、リフレクションで両方に差し込む。
    /// 毎レベルロードで行うこと。
    /// </summary>
    public static class ToolRegistration
    {
        public static T Register<T>() where T : ToolBase
        {
            var controller = ToolsModifierControl.toolController;
            if (controller == null)
            {
                Log.Warn("toolController not available; " + typeof(T).Name + " not registered");
                return null;
            }

            var tool = controller.GetComponent<T>() ?? controller.gameObject.AddComponent<T>();

            try
            {
                // 1. private な ToolController.m_tools 配列に足す
                var field = typeof(ToolController).GetField("m_tools",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                {
                    Log.Error("ToolController.m_tools field not found (game update?); "
                              + typeof(T).Name + " not registered", null);
                    return tool;
                }

                var tools = (ToolBase[])field.GetValue(controller);

                bool present = false;
                for (int i = 0; i < tools.Length; i++)
                {
                    if (tools[i] == tool) { present = true; break; }
                }

                if (!present)
                {
                    var grown = new ToolBase[tools.Length + 1];
                    Array.Copy(tools, grown, tools.Length);
                    grown[tools.Length] = tool;
                    field.SetValue(controller, grown);
                }

                // 2. 静的な ToolsModifierControl.m_Tools 辞書にも足す
                var dictField = typeof(ToolsModifierControl).GetField("m_Tools",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (dictField == null)
                {
                    Log.Error("ToolsModifierControl.m_Tools field not found (game update?); "
                              + typeof(T).Name + " not registered", null);
                    return tool;
                }

                var dict = dictField.GetValue(null) as Dictionary<Type, ToolBase>;
                if (dict == null)
                {
                    Log.Error("ToolsModifierControl.m_Tools was not a Dictionary<Type, ToolBase>; "
                              + typeof(T).Name + " not registered", null);
                    return tool;
                }
                dict[typeof(T)] = tool;

                Log.Info(typeof(T).Name + " registered");
            }
            catch (Exception e)
            {
                Log.Error("tool registration failed for " + typeof(T).Name, e);
            }

            return tool;
        }
    }
}
