using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace FrameRateControl
{
    [StaticConstructorOnStartup]
    public class FrameRateControlMod : Mod
    {
        public FrameRateControlMod(ModContentPack content) : base(content)
        {
            GetSettings<Settings>();

            QualitySettings.vSyncCount = 0;

            Settings.throttlingAvailable = TryToEnableThrottling();

            Settings.Apply();
        }

        bool TryToEnableThrottling()
        {
            var mod = LoadedModManager.RunningMods.FirstOrDefault(m => m.PackageId == "brrainz.harmony");
            if (mod == null) {
                return false;
            }

            void wrapperForSafety()
            {
                var harmony = new HarmonyLib.Harmony(Content.PackageId);
                {
                    var target = HarmonyLib.AccessTools.Method(typeof(RealTime), nameof(RealTime.Update));
                    var postfix = HarmonyLib.AccessTools.Method(typeof(FrameRateControlMod), nameof(ThrottleEngine));

                    harmony.Patch(target, postfix: new HarmonyLib.HarmonyMethod(postfix));
                }
                {
                    var target = HarmonyLib.AccessTools.Method(typeof(TickManager), nameof(TickManager.TickManagerUpdate));
                    var transpiler = HarmonyLib.AccessTools.Method(typeof(FrameRateControlMod), nameof(TickManagerUpdate_Transpiler));

                    harmony.Patch(target, transpiler: new HarmonyLib.HarmonyMethod(transpiler));
                }
            };

            try {
                wrapperForSafety();
            } catch (Exception e) {
                Log.Warning("FrameRateControl :: Despite HarmonyMod being loaded we can't patch, something went very wrong...\n" + e);
                return false;
            }

            return true;
        }

        static void ThrottleEngine()
        {
            if (!Settings.throttle) {
                return;
            }

            int snooze = (int) (Settings.targetSleepTime - Time.deltaTime);
            if (snooze > 0) {
                System.Threading.Thread.Sleep(snooze);
            }
        }

        static float WorstAllowedFPS()
        {
            if (Settings.throttle)
            {
                return 1000f / Settings.targetFrameRate; // 1.0-1.3 old code (float)clock.ElapsedMilliseconds > 1000f / WorstAllowedFPS). 1.3.3387 now field const!
            }
            else
            {
                return 1000f / 22f;
            }
        }

        static IEnumerable<CodeInstruction> TickManagerUpdate_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var allowedFps = HarmonyLib.AccessTools.Method(typeof(FrameRateControlMod), nameof(WorstAllowedFPS));
            foreach (var ci in instructions)
            {
                if (ci.opcode == OpCodes.Ldc_R4 && (float) ci.operand == 45.4545441f) // 1000f / 22f(const)
                {
                    yield return new CodeInstruction(OpCodes.Call, allowedFps);
                    Log.Message("FrameRateControl :: const field WorstAllowedFPS transpiled");
                }
                else yield return ci;
            }
        }

        // old code 1.0-1.3.3326
        //static void SetWorstAllowedFPS(ref float ___WorstAllowedFPS)
        //{
        //    if (Settings.throttle) {
        //        ___WorstAllowedFPS = Settings.targetFrameRate;
        //    } else {
        //        ___WorstAllowedFPS = 22f;
        //    }

        //}

        public override string SettingsCategory() => "Frame Rate Control";
        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoSettingsWindowContents(inRect);
    }

    class Settings : ModSettings
    {
        public static float targetFrameRate = 60;
        public static float targetSleepTime;

        public static bool throttle;
        public static bool throttlingAvailable;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref targetFrameRate, "targetFrameRate", 60);
            Scribe_Values.Look(ref throttle, "throttle", false);
        }

        public static void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard(GameFont.Small) {
                ColumnWidth = inRect.width
            };

            list.Begin(inRect);
            list.Label("Target Frame Rate:" + (Application.targetFrameRate == 0 ? "No Limit" : Application.targetFrameRate.ToString()), -1, "Default 60");
            targetFrameRate = list.Slider(targetFrameRate, 0f, 300f);
            if (throttlingAvailable && targetFrameRate > 0 && targetFrameRate <= 60) {
                list.CheckboxLabeled("Throttle Engine", ref throttle, "");
            }
            list.End();

            Apply();
        }

        public static void Apply()
        {
            if (targetFrameRate <= 0 || targetFrameRate >= 300) {
                targetFrameRate = Application.targetFrameRate = 0;
            } else {
                Application.targetFrameRate = (int) ((targetFrameRate + 2.5) / 5) * 5;
            }

            if (Application.targetFrameRate > 0 && Application.targetFrameRate <= 60) {
                targetSleepTime = (1000f / Application.targetFrameRate) - 1;
            } else {
                targetSleepTime = 22f;
            }
        }
    }
}
