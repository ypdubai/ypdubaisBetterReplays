using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine.InputSystem;
using System.Reflection;

namespace BetterReplays
{
    [Serializable]
    public class BetterReplaysConfig
    {
        private struct ValidationRule
        {
            public string fieldName;
            public float minValue;
            public float maxValue;
            public float defaultValue;
            public float sentinelValue;

            public ValidationRule(string fieldName, float minValue, float maxValue, float defaultValue, float sentinelValue = -1.0f)
            {
                this.fieldName = fieldName;
                this.minValue = minValue;
                this.maxValue = maxValue;
                this.defaultValue = defaultValue;
                this.sentinelValue = sentinelValue;
            }
        }

        // Validation rules for all float settings
        private static readonly ValidationRule[] validationRules = new ValidationRule[]
        {
            new ValidationRule("playerPositionSmoothing", 0.0f, 100.0f, 0.3f),
            new ValidationRule("pushBackSpeed", 0.0f, 100.0f, 0.01f),
            new ValidationRule("freeLookOrbitalLerpSpeed", 0.0f, 100.0f, 0.05f),
            new ValidationRule("freeLookExitPositionLerpSpeed", 0.0f, 100.0f, 0.1f),
            new ValidationRule("freeLookExitRotationLerpSpeed", 0.0f, 100.0f, 0.15f),
            new ValidationRule("minCameraDistance", 0.0f, 100.0f, 2.0f),
            new ValidationRule("freeLookTransitionDuration", 0.0f, 100.0f, 0.5f),
            new ValidationRule("firstPersonRotationLerpSpeed", 0.0f, 100.0f, 0.1f),
            new ValidationRule("cameraOffsetHeight", -100.0f, 100.0f, 0.5f, -999.0f),
            new ValidationRule("initialCameraDistance", 0.0f, 100.0f, 3.0f),
            new ValidationRule("firstPersonPitchLimit", 0.0f, 90.0f, 90.0f),
            new ValidationRule("freeLookPitchMin", -90.0f, 90.0f, -20.0f, -999.0f),
            new ValidationRule("freeLookPitchMax", -90.0f, 90.0f, 80.0f, -999.0f),
            new ValidationRule("zoomMinDistance", 0.0f, 100.0f, 0.2f),
            new ValidationRule("zoomMaxDistance", 0.0f, 1000.0f, 16.0f),
            new ValidationRule("zoomDefaultDistance", 0.0f, 100.0f, 1.0f),
            new ValidationRule("lerpSpeedAtMinZoom", 0.0f, 100.0f, 0.1f),
            new ValidationRule("lerpSpeedAtMaxZoom", 0.0f, 100.0f, 0.01f),
            new ValidationRule("lerpSpeedAtDefaultZoom", 0.0f, 100.0f, 0.02f),
            new ValidationRule("goalToPlayerRotationLerpSpeed", 0.0f, 100.0f, 0.05f)
        };

        // All settings are initialized to impossible values or null so we can
        // detect missing values from the config json.
        public float playerPositionSmoothing = -1.0f;
        public float pushBackSpeed = -1.0f;
        public float freeLookOrbitalLerpSpeed = -1.0f;
        public float freeLookExitPositionLerpSpeed = -1.0f;
        public float freeLookExitRotationLerpSpeed = -1.0f;
        public float minCameraDistance = -1.0f;
        public float freeLookTransitionDuration = -1.0f;
        public float firstPersonRotationLerpSpeed = -1.0f;
        public float cameraOffsetHeight = -999.0f;
        public float initialCameraDistance = -1.0f;
        public float firstPersonPitchLimit = -1.0f;
        public float freeLookPitchMin = -999.0f;
        public float freeLookPitchMax = -999.0f;
        public float zoomMinDistance = -1.0f;
        public float zoomMaxDistance = -1.0f;
        public float zoomDefaultDistance = -1.0f;
        public float lerpSpeedAtMinZoom = -1.0f;
        public float lerpSpeedAtMaxZoom = -1.0f;
        public float lerpSpeedAtDefaultZoom = -1.0f;
        public float goalToPlayerRotationLerpSpeed = -1.0f;
        public string toggleCameraKey = null;
        public string freeLookKey = null;
        public string freeLookMode = null;
        public bool rememberCameraState = false;

        private static string configPath = Path.Combine("config", "ypdubaisBetterReplays.jsonc");
        private static DateTime lastConfigWriteTime = DateTime.MinValue;

        public static BetterReplaysConfig LoadConfig()
        {
            BetterReplaysConfig config = new BetterReplaysConfig();

            try
            {
                if (File.Exists(configPath))
                {
                    lastConfigWriteTime = File.GetLastWriteTime(configPath);
                    string json = File.ReadAllText(configPath);
                    JsonConvert.PopulateObject(json, config);
                    BetterReplaysPlugin.Log("Configuration loaded from " + configPath);

                    config.CheckForMissingFields();
                }
                else
                {
                    BetterReplaysPlugin.Log("No configuration file found, creating default config at " + configPath);
                    config.SaveConfig();
                }
            }
            catch (Exception e)
            {
                BetterReplaysPlugin.LogError($"Failed to load configuration: {e.Message}. Using defaults.");
            }

            config.ValidateAndCorrect();

            return config;
        }

        public static void ForceRegenerate()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                    BetterReplaysPlugin.Log("Deleted existing config file to force regeneration");
                }

                BetterReplaysConfig config = new BetterReplaysConfig();
                config.SaveConfig();
                BetterReplaysPlugin.Log("Forced regeneration of config file with all default values");
            }
            catch (Exception e)
            {
                BetterReplaysPlugin.LogError($"Failed to force regenerate config: {e.Message}");
            }
        }

        public static bool HasConfigChanged()
        {
            if (!File.Exists(configPath))
                return false;

            DateTime currentWriteTime = File.GetLastWriteTime(configPath);
            if (currentWriteTime != lastConfigWriteTime)
            {
                lastConfigWriteTime = currentWriteTime;
                return true;
            }
            return false;
        }

        public void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));

                string json = JsonConvert.SerializeObject(this, Formatting.Indented);

                json = AddJsonComments(json);

                File.WriteAllText(configPath, json);
                lastConfigWriteTime = File.GetLastWriteTime(configPath);
                BetterReplaysPlugin.Log("Configuration saved to " + configPath);
            }
            catch (Exception e)
            {
                BetterReplaysPlugin.LogError($"Failed to save configuration: {e.Message}");
            }
        }

        private string AddJsonComments(string json)
        {
            string[] lines = json.Split('\n');
            string result = "{\n";
            result += "  // ========== Camera Settings ==========\n";
            result += "  // These control camera movement and behavior\n\n";

            for (int i = 1; i < lines.Length - 1; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();

                if (trimmed.StartsWith("\"playerPositionSmoothing\""))
                    result += "  // Player position smoothing factor (default: 0.3, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"pushBackSpeed\""))
                    result += "  // Speed for pushing camera back when too close (default: 0.01, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"freeLookOrbitalLerpSpeed\""))
                    result += "  // Free look orbital movement smoothing (default: 0.05, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"freeLookExitPositionLerpSpeed\""))
                    result += "  // Position lerp speed when exiting free look (default: 0.1, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"freeLookExitRotationLerpSpeed\""))
                    result += "  // Rotation lerp speed when exiting free look (default: 0.15, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"minCameraDistance\""))
                    result += "  // Minimum distance camera can be from player (default: 2.0, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"freeLookTransitionDuration\""))
                    result += "  // Time to transition into free look mode (default: 0.5, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"firstPersonRotationLerpSpeed\""))
                    result += "  // First person rotation smoothing (default: 0.1, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"cameraOffsetHeight\""))
                    result += "  // Height offset for third person camera (default: 0.5, range: -100.0-100.0)\n";
                else if (trimmed.StartsWith("\"initialCameraDistance\""))
                    result += "  // Initial camera distance when replay starts (default: 3.0, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"firstPersonPitchLimit\""))
                    result += "  // Maximum pitch angle in first person (default: 90, range: 0-90)\n";
                else if (trimmed.StartsWith("\"freeLookPitchMin\""))
                    result += "  // Minimum pitch angle in free look (default: -20, range: -90-90)\n";
                else if (trimmed.StartsWith("\"freeLookPitchMax\""))
                    result += "  // Maximum pitch angle in free look (default: 80, range: -90-90)\n";
                else if (trimmed.StartsWith("\"zoomMinDistance\""))
                    result += "  // Minimum zoom distance (default: 0.2, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"zoomMaxDistance\""))
                    result += "  // Maximum zoom distance (default: 16.0, range: 0.0-1000.0)\n";
                else if (trimmed.StartsWith("\"zoomDefaultDistance\""))
                    result += "  // Default zoom distance (default: 1.0, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"lerpSpeedAtMinZoom\""))
                    result += "  // Camera lerp speed when zoomed in closest (default: 0.1, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"lerpSpeedAtMaxZoom\""))
                    result += "  // Camera lerp speed when zoomed out farthest (default: 0.01, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"lerpSpeedAtDefaultZoom\""))
                    result += "  // Camera lerp speed at default zoom (default: 0.02, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"goalToPlayerRotationLerpSpeed\""))
                    result += "  // Camera rotation lerp speed for goal-to-player transition (default: 0.05, range: 0.0-100.0)\n";
                else if (trimmed.StartsWith("\"toggleCameraKey\""))
                {
                    result += "\n  // ========== Keybind Settings ==========\n";
                    result += "  // Key bindings for camera controls\n";
                    result += "  // Valid keys: a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, p, q, r, s, t, u, v, w, x, y, z,\n";
                    result += "  //             digit0, digit1, digit2, digit3, digit4, digit5, digit6, digit7, digit8, digit9,\n";
                    result += "  //             f1, f2, f3, f4, f5, f6, f7, f8, f9, f10, f11, f12,\n";
                    result += "  //             space, tab, enter, escape, backspace, delete, insert, home, end, pageUp, pageDown,\n";
                    result += "  //             upArrow, downArrow, leftArrow, rightArrow, leftShift, rightShift, leftCtrl, rightCtrl,\n";
                    result += "  //             leftAlt, rightAlt, leftMeta, rightMeta, capsLock, numLock, scrollLock, printScreen,\n";
                    result += "  //             pause, numpad0, numpad1, numpad2, numpad3, numpad4, numpad5, numpad6, numpad7,\n";
                    result += "  //             numpad8, numpad9, numpadDivide, numpadMultiply, numpadMinus, numpadPlus,\n";
                    result += "  //             numpadEnter, numpadPeriod, semicolon, comma, period, slash, backslash,\n";
                    result += "  //             leftBracket, rightBracket, quote, backquote, minus, equals\n";
                    result += "  // Valid mouse buttons: leftButton, rightButton, middleButton, forwardButton, backButton\n\n";
                    result += "  // Key or mouse button to toggle between first/third person (default: \"c\")\n";
                }
                else if (trimmed.StartsWith("\"freeLookKey\""))
                    result += "  // Key or mouse button for free look (default: \"rightButton\")\n";
                else if (trimmed.StartsWith("\"freeLookMode\""))
                {
                    result += "\n  // ========== Misc Settings ==========\n";
                    result += "  // General behavior settings\n\n";
                    result += "  // Free look input mode (default: \"hold\", options: \"hold\", \"toggle\")\n";
                }
                else if (trimmed.StartsWith("\"rememberCameraState\""))
                    result += "  // Remember camera mode and zoom between replays (default: false)\n";

                result += line + "\n";
            }

            result += "}";
            return result;
        }

        private void CheckForMissingFields()
        {
            bool needsSave = false;

            foreach (var rule in validationRules)
            {
                FieldInfo field = typeof(BetterReplaysConfig).GetField(rule.fieldName);
                if (field != null && field.FieldType == typeof(float))
                {
                    float value = (float)field.GetValue(this);
                    if (Math.Abs(value - rule.sentinelValue) < 0.001f)
                    {
                        BetterReplaysPlugin.LogWarning($"Setting {rule.fieldName} is missing from ypdubaisBetterReplays.jsonc, regenerating default value ({rule.defaultValue})");
                        field.SetValue(this, rule.defaultValue);
                        BetterReplaysPlugin.Log($"Applied regenerated value: {rule.fieldName} = {rule.defaultValue}");
                        needsSave = true;
                    }
                }
            }

            // Check keybind settings
            if (string.IsNullOrEmpty(toggleCameraKey))
            {
                BetterReplaysPlugin.LogWarning("Setting toggleCameraKey is missing from ypdubaisBetterReplays.jsonc, regenerating default value (\"c\")");
                toggleCameraKey = "c";
                BetterReplaysPlugin.Log("Applied regenerated value: toggleCameraKey = \"c\"");
                needsSave = true;
            }

            if (string.IsNullOrEmpty(freeLookKey))
            {
                BetterReplaysPlugin.LogWarning("Setting freeLookKey is missing from ypdubaisBetterReplays.jsonc, regenerating default value (\"rightButton\")");
                freeLookKey = "rightButton";
                BetterReplaysPlugin.Log("Applied regenerated value: freeLookKey = \"rightButton\"");
                needsSave = true;
            }

            // Check misc settings
            if (string.IsNullOrEmpty(freeLookMode))
            {
                BetterReplaysPlugin.LogWarning("Setting freeLookMode is missing from ypdubaisBetterReplays.jsonc, regenerating default value (\"hold\")");
                freeLookMode = "hold";
                BetterReplaysPlugin.Log("Applied regenerated value: freeLookMode = \"hold\"");
                needsSave = true;
            }

            if (needsSave)
            {
                BetterReplaysPlugin.Log("Saving config with regenerated values...");
                SaveConfig();
                BetterReplaysPlugin.Log("Config saved. Regenerated values are now active in current config instance.");
            }
        }

        private void ValidateAndCorrect()
        {
            bool needsSave = false;

            foreach (var rule in validationRules)
            {
                FieldInfo field = typeof(BetterReplaysConfig).GetField(rule.fieldName);
                if (field != null && field.FieldType == typeof(float))
                {
                    float value = (float)field.GetValue(this);
                    if (value < rule.minValue || value > rule.maxValue)
                    {
                        BetterReplaysPlugin.LogWarning($"Setting {rule.fieldName} is invalid in ypdubaisBetterReplays.jsonc ({value}), regenerating default value ({rule.defaultValue})");
                        field.SetValue(this, rule.defaultValue);
                        BetterReplaysPlugin.Log($"Applied corrected value: {rule.fieldName} = {rule.defaultValue}");
                        needsSave = true;
                    }
                }
            }

            // Validate keybind settings
            if (string.IsNullOrEmpty(toggleCameraKey) || !IsValidKeyBinding(toggleCameraKey))
            {
                if (string.IsNullOrEmpty(toggleCameraKey))
                {
                    BetterReplaysPlugin.LogWarning("Setting toggleCameraKey is missing from ypdubaisBetterReplays.jsonc, regenerating default value (\"c\")");
                }
                else
                {
                    BetterReplaysPlugin.LogWarning($"Setting toggleCameraKey is invalid in ypdubaisBetterReplays.jsonc (\"{toggleCameraKey}\"), regenerating default value (\"c\")");
                }
                toggleCameraKey = "c";
                BetterReplaysPlugin.Log("Applied corrected value: toggleCameraKey = \"c\"");
                needsSave = true;
            }

            if (string.IsNullOrEmpty(freeLookKey) || !IsValidKeyBinding(freeLookKey))
            {
                if (string.IsNullOrEmpty(freeLookKey))
                {
                    BetterReplaysPlugin.LogWarning("Setting freeLookKey is missing from ypdubaisBetterReplays.jsonc, regenerating default value (\"rightButton\")");
                }
                else
                {
                    BetterReplaysPlugin.LogWarning($"Setting freeLookKey is invalid in ypdubaisBetterReplays.jsonc (\"{freeLookKey}\"), regenerating default value (\"rightButton\")");
                }
                freeLookKey = "rightButton";
                BetterReplaysPlugin.Log("Applied corrected value: freeLookKey = \"rightButton\"");
                needsSave = true;
            }

            // Validate misc settings
            if (string.IsNullOrEmpty(freeLookMode) ||
                (!freeLookMode.Equals("hold") && !freeLookMode.Equals("toggle")))
            {
                if (string.IsNullOrEmpty(freeLookMode))
                {
                    BetterReplaysPlugin.LogWarning("Setting freeLookMode is missing from ypdubaisBetterReplays.jsonc, regenerating default value (\"hold\")");
                }
                else
                {
                    BetterReplaysPlugin.LogWarning($"Setting freeLookMode is invalid in ypdubaisBetterReplays.jsonc (\"{freeLookMode}\"), regenerating default value (\"hold\")");
                }
                freeLookMode = "hold";
                BetterReplaysPlugin.Log("Applied corrected value: freeLookMode = \"hold\"");
                needsSave = true;
            }

            if (needsSave)
            {
                BetterReplaysPlugin.Log("Saving config with corrected values...");
                SaveConfig();
                BetterReplaysPlugin.Log("Config saved. Corrected values are now active in current config instance.");
            }
        }

        private bool IsValidKeyBinding(string keyBinding)
        {
            if (string.IsNullOrEmpty(keyBinding))
                return false;

            try
            {
                Enum.Parse(typeof(Key), keyBinding, true);
                return true;
            }
            catch
            {
                return keyBinding.Equals("leftButton", StringComparison.OrdinalIgnoreCase) ||
                       keyBinding.Equals("rightButton", StringComparison.OrdinalIgnoreCase) ||
                       keyBinding.Equals("middleButton", StringComparison.OrdinalIgnoreCase) ||
                       keyBinding.Equals("forwardButton", StringComparison.OrdinalIgnoreCase) ||
                       keyBinding.Equals("backButton", StringComparison.OrdinalIgnoreCase);
            }
        }

        public Key GetToggleCameraKey()
        {
            if (IsToggleCameraMouseButton())
            {
                return Key.None;
            }

            try
            {
                return (Key)Enum.Parse(typeof(Key), toggleCameraKey, true);
            }
            catch
            {
                BetterReplaysPlugin.LogWarning($"Invalid toggle camera key '{toggleCameraKey}', cannot parse as keyboard key, using default 'c'");
                return Key.C;
            }
        }

        public bool IsToggleCameraMouseButton()
        {
            return toggleCameraKey.Equals("leftButton", StringComparison.OrdinalIgnoreCase) ||
                   toggleCameraKey.Equals("rightButton", StringComparison.OrdinalIgnoreCase) ||
                   toggleCameraKey.Equals("middleButton", StringComparison.OrdinalIgnoreCase) ||
                   toggleCameraKey.Equals("forwardButton", StringComparison.OrdinalIgnoreCase) ||
                   toggleCameraKey.Equals("backButton", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsFreeLookMouseButton()
        {
            return freeLookKey.Equals("leftButton", StringComparison.OrdinalIgnoreCase) ||
                   freeLookKey.Equals("rightButton", StringComparison.OrdinalIgnoreCase) ||
                   freeLookKey.Equals("middleButton", StringComparison.OrdinalIgnoreCase) ||
                   freeLookKey.Equals("forwardButton", StringComparison.OrdinalIgnoreCase) ||
                   freeLookKey.Equals("backButton", StringComparison.OrdinalIgnoreCase);
        }

        public Key GetFreeLookKey()
        {
            if (IsFreeLookMouseButton())
            {
                return Key.None;
            }

            try
            {
                return (Key)Enum.Parse(typeof(Key), freeLookKey, true);
            }
            catch
            {
                BetterReplaysPlugin.LogWarning($"Invalid free look key '{freeLookKey}', cannot parse as keyboard key");
                return Key.None;
            }
        }

        public bool IsFreeLookToggle()
        {
            return freeLookMode.Equals("toggle");
        }
    }
}
