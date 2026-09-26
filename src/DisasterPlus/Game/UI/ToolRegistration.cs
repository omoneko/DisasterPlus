using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Makes ToolController aware of a custom ToolBase.
    ///
    /// ToolController.m_tools is built exactly once in Awake from GetComponents&lt;ToolBase&gt;(),
    /// and ToolsModifierControl.SetTool&lt;T&gt; only does a TryGetValue on a static dictionary.
    /// Neither knows about a tool added after startup, so insert into both by reflection.
    /// Do this on every level load.
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
                // 1. add to the private ToolController.m_tools array
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

                // 2. add to the static ToolsModifierControl.m_Tools dictionary as well
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
