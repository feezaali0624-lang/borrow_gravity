using System;
using System.Collections.Generic;
using UnityEngine;

namespace BorrowGravity
{
    /// <summary>
    /// In-game HUD, built only from the art kit's HUD design (<see cref="HudIcons"/> + <see cref="UIKit"/>):
    /// power rings for A (sun) and B (glow), objective banner, gravity indicator, danger banner with countdown ring,
    /// checkpoint banner, engine meter + tilt gauge, player tags / off-screen arrows, and the story / pause /
    /// stage-complete screens. Other scripts talk to it through the static methods.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        static GameUI instance;

        public HudIcons icons;
        public PlayerController playerA, playerB;
        public GravityPower gravityPower;
        public NullifyPower nullifyPower;
        public string controlsA = "WASD / arrows move · mouse or drag to look · Space jump · E switch zone gravity";
        public string controlsB = "WASD / arrows move · mouse or drag to look · Space jump · E tap: nullify, hold: steer";

        class Timed { public string text; public float until; public HudHazard hazard; public float countdownEnd; public float start; }
        Timed warning, checkpoint, message, hint;
        readonly Dictionary<PlayerController, Timed> toasts = new Dictionary<PlayerController, Timed>();
        string countdownLabel;
        Func<float> countdownValue;
        HudObjective objective = HudObjective.ReachStation;
        string objectiveText = "";
        string sectionTitle = "";
        float sectionUntil;
        bool showHelp = false;

        void Awake()
        {
            instance = this;
            useGUILayout = false; // no GUILayout here: skip IMGUI's extra layout pass every frame
        }

        // ---------- static API ----------

        /// <summary>Seconds left on the running countdown, or -1 (streamed to the joining phone).</summary>
        public static float CountdownLeft => instance != null && instance.countdownValue != null ? Mathf.Max(0f, instance.countdownValue()) : -1f;

        public static void ShowWarning(HudHazard hazard, string text, float countdown, float duration)
        {
            NetSession.UiWarning(hazard, text, countdown, duration);
            if (instance == null) return;
            instance.warning = new Timed { text = text, hazard = hazard, start = Time.time, until = Time.time + Mathf.Max(duration, countdown), countdownEnd = countdown > 0f ? Time.time + countdown : 0f };
        }

        public static void ShowCheckpoint(string label)
        {
            NetSession.UiText(1, label);
            if (instance != null) instance.checkpoint = new Timed { text = label, start = Time.time, until = Time.time + 2.6f };
        }

        public static void ShowMessage(string text, float duration)
        {
            NetSession.UiText(2, text, duration);
            if (instance != null) instance.message = new Timed { text = text, start = Time.time, until = Time.time + duration };
        }

        /// <summary>How long an instruction notification stays on screen (seconds).</summary>
        public const float HintSeconds = 3f;

        public static void ShowHint(string text, float duration)
        {
            duration = Mathf.Min(duration, HintSeconds);
            NetSession.UiText(3, text, duration);
            if (instance != null) instance.hint = new Timed { text = text, start = Time.time, until = Time.time + duration };
        }

        public static void Toast(PlayerController p, string text)
        {
            if (p != null) NetSession.UiToast(p.playerIndex, text);
            if (instance != null && p != null) instance.toasts[p] = new Timed { text = text, until = Time.time + 1.8f };
        }

        public static void SetObjective(HudObjective o, string text)
        {
            NetSession.UiObjective(o, text);
            if (instance == null) return;
            instance.objective = o;
            instance.objectiveText = text;
        }

        public static void SetSection(string title)
        {
            NetSession.UiText(6, title);
            if (instance == null) return;
            instance.sectionTitle = title;
            instance.sectionUntil = Time.time + 5f;
        }

        public static void StartCountdown(string label, HudHazard hazard, Func<float> secondsLeft)
        {
            NetSession.UiCountdown(label, hazard, true);
            if (instance == null) return;
            instance.countdownLabel = label;
            instance.countdownValue = secondsLeft;
        }

        public static void StopCountdown()
        {
            NetSession.UiCountdown("", HudHazard.General, false);
            if (instance != null) instance.countdownValue = null;
        }

        void Update()
        {
            if (PlayerKeys.Pressed(UnityEngine.InputSystem.Key.H)) showHelp = !showHelp;
        }

        // ---------- drawing ----------

        static float Fade(Timed t, float inTime = 0.2f, float outTime = 0.35f)
        {
            if (t == null) return 0f;
            float a = Mathf.Clamp01((Time.time - t.start) / inTime);
            float b = Mathf.Clamp01((t.until - Time.time) / outTime);
            return Mathf.Min(a, b);
        }

        void OnGUI()
        {
            UIKit.Begin();
            float w = UIKit.Width, h = UIKit.Height;
            var sm = StageManager.Instance;
            if (WantsBackdrop(sm))
            {
                if (capturing) return; // nothing over the game while its still is taken
                if (frosted != null) GUI.DrawTexture(new Rect(0, 0, w, h), frosted, ScaleMode.StretchToFill);
                UIKit.Fill(new Rect(0, 0, w, h), new Color(0.02f, 0.03f, 0.07f, frosted != null ? 0.58f : 0.72f));
                if (sm.MissionShowing) DrawMission(w, h, sm);
                else if (sm.State == StageState.Intro) DrawIntro(w, h, sm);
                else if (sm.State == StageState.Paused) DrawPause(w, h, sm);
                else DrawComplete(w, h, sm);
                return;
            }
            SettingsOpen = false;

            // split-screen divider (offline with two players; online / solo each screen shows one player)
            if (!NetSession.Online && !GameFlow.Solo)
            {
                MenuStyle.Line(w * 0.5f - 0.5f, 0, 1f, MenuStyle.W50, h);
            }
            DrawWorldMarkers();
            DrawPanels(w);
            DrawPauseButton(w, sm);
            DrawCameraButton(w, sm);
            // the centre banners stack top-down in this order, so two at once never overlap
            // (the pause / camera buttons are on the right edge now, so the column starts near the top)
            slot = 20f;
            DrawObjective(w, sm);
            DrawEngine(w, h);
            DrawMessages(w, h);
            DrawWarning(w);
            DrawAutoZoneWarning(w);
            DrawCheckpoint(w);
            DrawCountdown(w);
            DrawHelp(w, h, sm);
        }

        float slot;

        /// <summary>The next free spot (top y) in the centre column of banners; the next one goes below it.</summary>
        float Slot(float height)
        {
            float y = slot;
            slot += height + 10f;
            return y;
        }

        // ---------- frosted backdrop: a blurred still of the game behind the pause menu and the stage cards ----------

        RenderTexture frosted;
        bool capturing;

        static bool WantsBackdrop(StageManager sm) =>
            sm != null && sm.isActiveAndEnabled && (sm.MissionShowing || sm.State == StageState.Intro || sm.State == StageState.Paused || sm.State == StageState.Complete);

        void LateUpdate()
        {
            bool want = WantsBackdrop(StageManager.Instance);
            if (want && frosted == null && !capturing) StartCoroutine(CaptureBackdrop());
            else if (!want && frosted != null && !capturing) ReleaseBackdrop();
        }

        void OnDisable() { capturing = false; ReleaseBackdrop(); }

        void ReleaseBackdrop()
        {
            if (frosted == null) return;
            frosted.Release();
            Destroy(frosted);
            frosted = null;
        }

        System.Collections.IEnumerator CaptureBackdrop()
        {
            capturing = true;
            yield return new WaitForEndOfFrame();
            Texture2D shot = null;
            try { shot = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (System.Exception e) { Debug.LogWarning("[UI] backdrop capture failed: " + e.Message); }
            if (shot != null)
            {
                if (WantsBackdrop(StageManager.Instance)) frosted = Blur(shot);
                Destroy(shot);
            }
            capturing = false;
        }

        /// <summary>Halve the image a few times (each bilinear halving averages 2x2 pixels), then scale it back up
        /// step by step: a cheap, smooth blur that works on every phone.</summary>
        static RenderTexture Blur(Texture src)
        {
            int w = Mathf.Max(8, src.width / 2), h = Mathf.Max(8, src.height / 2);
            var cur = RenderTexture.GetTemporary(w, h, 0);
            Graphics.Blit(src, cur);
            for (int i = 0; i < 4; i++)
            {
                w = Mathf.Max(4, w / 2); h = Mathf.Max(4, h / 2);
                var next = RenderTexture.GetTemporary(w, h, 0);
                Graphics.Blit(cur, next);
                RenderTexture.ReleaseTemporary(cur);
                cur = next;
            }
            for (int i = 0; i < 2; i++)
            {
                var up = RenderTexture.GetTemporary(w * 2, h * 2, 0);
                Graphics.Blit(cur, up);
                RenderTexture.ReleaseTemporary(cur);
                cur = up;
                w *= 2; h *= 2;
            }
            var result = new RenderTexture(w * 2, h * 2, 0) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Graphics.Blit(cur, result);
            RenderTexture.ReleaseTemporary(cur);
            return result;
        }

        Texture2D GravityIcon(GravityMode m)
        {
            if (icons == null) return null;
            switch (m)
            {
                case GravityMode.Up: return icons.Gravity_Up;
                case GravityMode.Zero: return icons.Gravity_Zero;
                case GravityMode.Left: return icons.Gravity_Left;
                case GravityMode.Right: return icons.Gravity_Right;
                case GravityMode.Forward:
                case GravityMode.Back: return icons.Gravity_Off;
                default: return icons.Gravity_Down;
            }
        }

        // ---------- HUD in the main-menu style: white hairlines and letter-spaced white text only ----------

        /// <summary>Plain words for a zone's gravity: ON or OFF first, and which way only when it isn't the usual down.</summary>
        static string ModeLabel(GravityMode m)
        {
            switch (m)
            {
                case GravityMode.Down: return "GRAVITY ON";
                case GravityMode.Zero: return "GRAVITY OFF";
                case GravityMode.Up: return "GRAVITY ON · UPSIDE DOWN";
                case GravityMode.RadialIn: return "GRAVITY ON · TO RING";
                case GravityMode.RadialOut: return "GRAVITY ON · OUTWARD";
                default: return "GRAVITY ON · SIDEWAYS";
            }
        }

        /// <summary>A player's power panel: outline + brackets, a dotted cooldown ring with the letter, name and status.</summary>
        void PowerPanel(Rect panel, string letter, string role, float fill, bool activeGlow, string line1, string line2, bool ringRight)
        {
            MenuStyle.Glass(panel);
            MenuStyle.Box(panel, MenuStyle.W25, 1f);
            MenuStyle.Brackets(panel);
            var ringC = new Vector2(ringRight ? panel.xMax - 50f : panel.x + 50f, panel.center.y);
            MenuStyle.Box(new Rect(ringC.x - 30f, ringC.y - 30f, 60f, 60f), MenuStyle.W25, 1f, 30f);
            MenuStyle.Arc(ringC, 37f, fill, activeGlow ? 1f : 0.8f);
            MenuStyle.Text(new Rect(ringC.x - 30f, ringC.y - 30f, 60f, 60f), letter, UIKit.Face.Display, 26, 1f);
            float tx = ringRight ? panel.x + 16f : panel.x + 100f, tw = panel.width - 116f;
            MenuStyle.Text(new Rect(tx, panel.y + 12f, tw, 12f), "PLAYER " + letter, UIKit.Face.Heavy, 10, MenuStyle.W50, 0.3f, TextAnchor.UpperLeft);
            MenuStyle.Text(new Rect(tx, panel.y + 28f, tw, 20f), role, UIKit.Face.Heavy, 15, 1f, 0.08f, TextAnchor.UpperLeft);
            MenuStyle.Text(new Rect(tx, panel.y + 54f, tw, 18f), line1, UIKit.Face.Heavy, 13, MenuStyle.W80, 0.04f, TextAnchor.UpperLeft);
            MenuStyle.Text(new Rect(tx, panel.y + 76f, tw, 16f), line2, UIKit.Face.Body, 11, MenuStyle.W50, 0f, TextAnchor.UpperLeft);
        }

        /// <summary>What a player's power panel shows (computed on the host / offline, streamed to joining devices).</summary>
        public struct PanelInfo
        {
            public string role, line1, line2;
            public float fill;
            public bool active;
        }

        public static PanelInfo PanelFor(PlayerController p)
        {
            var info = new PanelInfo { role = "", line1 = "", line2 = "", fill = 1f };
            if (p == null) return info;
            var gp = p.GetComponent<GravityPower>();
            var np = p.GetComponent<NullifyPower>();
            bool g = gp != null && gp.enabled, n = np != null && np.enabled;
            string nullLine = "";
            if (n)
            {
                var act = np.Active;
                nullLine = act != null ? "HOLDING  " + act.displayName.ToUpperInvariant()
                         : np.CooldownRemaining > 0f ? "RECHARGING"
                         : np.Target != null ? "TARGET  " + np.Target.displayName.ToUpperInvariant() : "NO TARGET IN FRONT";
                info.active = act != null;
            }
            if (g && n)
            {
                var zone = gp.Target;
                info.role = "GRAVITY + NULLIFY";
                info.fill = 1f - gp.CooldownFraction;
                info.line1 = zone != null ? "E  " + ModeLabel(zone.Mode) : "E  NO ZONE NEARBY";
                info.line2 = "Q  " + nullLine.ToLowerInvariant();
            }
            else if (g)
            {
                var zone = gp.Target;
                info.role = "GRAVITY CONTROLLER";
                info.fill = 1f - gp.CooldownFraction;
                info.line1 = zone != null ? ModeLabel(zone.Mode) : "NO ZONE NEARBY";
                info.line2 = zone != null ? "E · switch gravity on / off" : "";
            }
            else if (n)
            {
                var act = np.Active;
                info.role = "GRAVITY NULLIFIER";
                info.fill = act != null ? act.RemainingFraction : 1f - np.CooldownFraction;
                info.line1 = nullLine;
                info.line2 = act != null ? "Hold POWER: steer · tap: drop" : np.CooldownRemaining > 0f ? "" : "E · nullify";
            }
            return info;
        }

        /// <summary>The players whose panels this device shows: its own online, A (solo) or A + B offline.</summary>
        static System.Collections.Generic.IEnumerable<int> LocalPlayers()
        {
            if (NetSession.Online) { yield return NetSession.LocalPlayer; yield break; }
            yield return 0;
            if (!GameFlow.Solo) yield return 1;
        }

        void DrawPanels(float w)
        {
            int n = 0;
            foreach (int i in LocalPlayers())
            {
                var info = NetSession.IsClient ? NetHud.Panel(i) : PanelFor(Players.Get(i));
                if (string.IsNullOrEmpty(info.role)) continue;
                bool right = n == 1;
                var rect = right ? new Rect(w - 18f - 320f, 16f, 320f, 104f) : new Rect(18f, 16f, 320f, 104f);
                PowerPanel(rect, GameFlow.Letter(i), info.role, info.fill, info.active, info.line1, info.line2, right);
                n++;
            }
        }

        /// <summary>
        /// The objective banner, shown during play in the centre column.
        /// Uses the text the stage set through <see cref="SetObjective"/>, or the StageManager's own objective text.
        /// </summary>
        void DrawObjective(float w, StageManager sm)
        {
            if (sm == null || sm.State != StageState.Playing) return;
            string text = !string.IsNullOrEmpty(objectiveText) ? objectiveText : sm.objectiveText;
            if (string.IsNullOrEmpty(text)) return;

            bool sectionOn = Time.time < sectionUntil && !string.IsNullOrEmpty(sectionTitle);
            var r = new Rect(w * 0.5f - 230, Slot(74 + (sectionOn ? 24f : 0f)), 460, 74);
            MenuStyle.Glass(r);
            MenuStyle.Box(r, MenuStyle.W25, 1f);
            MenuStyle.Brackets(r);
            MenuStyle.Text(new Rect(r.x, r.y + 12, r.width, 12), StageShortTitle(sm.stageTitle).ToUpperInvariant(), UIKit.Face.Heavy, 10, MenuStyle.W50, 0.3f, TextAnchor.UpperCenter);
            MenuStyle.Text(new Rect(r.x, r.y + 30, r.width, 26), text.ToUpperInvariant(), UIKit.Face.Display, 20, 1f, 0.1f, TextAnchor.UpperCenter);
            if (!string.IsNullOrEmpty(TimeOfDay.CurrentLabel))
                MenuStyle.Text(new Rect(r.xMax - 110, r.yMax - 16, 100, 12), TimeOfDay.CurrentLabel.ToUpperInvariant(), UIKit.Face.Heavy, 9, MenuStyle.W50, 0.3f, TextAnchor.UpperRight);
            if (sectionOn)
            {
                float a = Mathf.Clamp01(sectionUntil - Time.time);
                MenuStyle.Text(new Rect(w * 0.5f - 300, r.yMax + 6, 600, 18), sectionTitle.ToUpperInvariant(), UIKit.Face.Heavy, 12, MenuStyle.W80 * a, 0.25f, TextAnchor.UpperCenter);
            }
        }

        static string StageShortTitle(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            int i = t.IndexOf('—');
            return i > 0 ? t.Substring(0, i).Trim() + "  ·  " + t.Substring(i + 1).Trim() : t;
        }

        void DrawAutoZoneWarning(float w)
        {
            foreach (var p in Players.All)
            {
                if (p == null) continue;
                var z = GravityZone.FindZone(p.transform.position, true);
                if (z == null || !z.autoCycle || !z.AutoWarningActive) continue;
                var r = new Rect(w * 0.5f - 190, Slot(56), 380, 56);
                float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 5f));
                MenuStyle.Box(r, pulse, 1f);
                MenuStyle.Brackets(r);
                MenuStyle.Text(new Rect(r.x, r.y + 8, r.width, 20), $"GRAVITY SHIFT IN {Mathf.CeilToInt(z.AutoTimeLeft)}", UIKit.Face.Display, 16, 1f, 0.15f, TextAnchor.UpperCenter);
                MenuStyle.Text(new Rect(r.x, r.y + 32, r.width, 14), "Get over a safe grip!", UIKit.Face.Body, 12, MenuStyle.W80, 0f, TextAnchor.UpperCenter);
                return;
            }
        }

        void DrawCountdown(float w)
        {
            if (countdownValue == null) return;
            float left = Mathf.Max(0f, countdownValue());
            var c = new Vector2(w * 0.5f, Slot(128) + 52f);
            bool urgent = left < 10f;
            float a = urgent ? 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 6f)) : 1f;
            MenuStyle.Box(new Rect(c.x - 52, c.y - 52, 104, 104), MenuStyle.W50 * a, 1f, 52f);
            MenuStyle.Arc(c, 60f, (left % 60f) / 60f, a);
            MenuStyle.Text(new Rect(c.x - 52, c.y - 52, 104, 104), Mathf.CeilToInt(left).ToString(), UIKit.Face.Display, 34, a);
            MenuStyle.Text(new Rect(c.x - 220, c.y + 70, 440, 16), (countdownLabel ?? "").ToUpperInvariant(), UIKit.Face.Heavy, 12, MenuStyle.W80, 0.3f, TextAnchor.UpperCenter);
        }

        void DrawWarning(float w)
        {
            float a = Fade(warning);
            if (a <= 0f) return;
            var r = new Rect(w * 0.5f - 300 + (1 - a) * 40f, Slot(84), 600, 84);
            float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 5f));
            MenuStyle.Box(r, pulse * a, 1f);
            MenuStyle.Brackets(r, 12f, 2f, a);
            MenuStyle.Text(new Rect(r.x + 18, r.y + 12, 200, 12), "WARNING", UIKit.Face.Heavy, 10, MenuStyle.W50 * a, 0.3f, TextAnchor.UpperLeft);
            bool counting = warning.countdownEnd > Time.time;
            float right = counting ? 90f : 18f;
            UIKit.Text(new Rect(r.x + 18, r.y + 28, r.width - 18 - right, r.height - 34), warning.text, UIKit.Style(UIKit.Face.Heavy, 16, MenuStyle.White(a), TextAnchor.UpperLeft, true));
            if (counting)
            {
                var c = new Vector2(r.xMax - 46, r.center.y);
                MenuStyle.Box(new Rect(c.x - 28, c.y - 28, 56, 56), MenuStyle.W50 * a, 1f, 28f);
                MenuStyle.Text(new Rect(c.x - 28, c.y - 28, 56, 56), Mathf.CeilToInt(warning.countdownEnd - Time.time).ToString(), UIKit.Face.Display, 22, a);
            }
        }

        void DrawCheckpoint(float w)
        {
            float a = Fade(checkpoint);
            if (a <= 0f) return;
            var r = new Rect(w * 0.5f - 220, Slot(66) - (1 - a) * 20f, 440, 66);
            MenuStyle.Box(r, MenuStyle.W50 * a, 1f);
            MenuStyle.Brackets(r, 12f, 2f, a);
            MenuStyle.Text(new Rect(r.x, r.y + 12, r.width, 12), "CHECKPOINT", UIKit.Face.Heavy, 10, MenuStyle.W50 * a, 0.3f, TextAnchor.UpperCenter);
            MenuStyle.Text(new Rect(r.x, r.y + 30, r.width, 22), checkpoint.text.ToUpperInvariant(), UIKit.Face.Display, 18, a, 0.1f, TextAnchor.UpperCenter);
        }

        void DrawEngine(float w, float h)
        {
            var e = EngineCarrier.Instance;
            if (e == null) return;
            bool remote = NetSession.IsClient;
            float stab = remote ? NetHud.engineStability01 : e.Stability01;
            var r = new Rect(w * 0.5f - 170, Slot(34), 340, 34);
            MenuStyle.Box(r, MenuStyle.W25, 1f);
            UIKit.Fill(new Rect(r.x + 4, r.y + 4, (r.width - 8) * stab, r.height - 8), MenuStyle.White(0.35f));
            MenuStyle.Text(r, "ENGINE STABILITY  " + Mathf.RoundToInt(stab * 100) + "%", UIKit.Face.Heavy, 11, 1f, 0.25f);
        }

        /// <summary>Where each engine half is (carried / installed), under this screen's power panel.</summary>
        void DrawParts(float w)
        {
            int mask = NetSession.IsClient ? NetHud.partsMask : PartsMask();
            if (mask == 0) return;
            bool right = NetSession.IsClient;
            var r = new Rect(right ? w - 18 - 320 : 18, 128, 320, 34);
            MenuStyle.Glass(r);
            MenuStyle.Box(r, MenuStyle.W25, 1f);
            MenuStyle.Text(new Rect(r.x + 14, r.y, 80, r.height), "ENGINE", UIKit.Face.Heavy, 10, MenuStyle.W50, 0.3f, TextAnchor.MiddleLeft);
            for (int p = 0; p < 2; p++)
            {
                int state = (mask >> (p * 2)) & 3; // 1 waiting, 2 held, 3 installed
                if (state == 0) continue; // no half for this player (solo)
                string label = (p == 0 ? "A  " : "B  ") + (state == 3 ? "INSTALLED" : state == 2 ? "IN HAND" : "PICK UP");
                var cell = new Rect(r.x + 90 + p * 112, r.y, 108, r.height);
                var dot = new Rect(cell.x, cell.center.y - 5, 10, 10);
                if (state >= 2) MenuStyle.Box(dot, 1f, 0f, 5f); else MenuStyle.Box(dot, MenuStyle.W50, 1f, 5f);
                MenuStyle.Text(new Rect(cell.x + 16, cell.y, cell.width - 16, cell.height), label, UIKit.Face.Heavy, 10, state == 1 ? MenuStyle.W50 : 1f, 0.12f, TextAnchor.MiddleLeft);
            }
        }

        /// <summary>Two bits per half (A low, B high): 0 none, 1 waiting, 2 held, 3 installed.</summary>
        public static int PartsMask()
        {
            int mask = 0;
            foreach (var part in EnginePart.All)
            {
                int s = part.State == EnginePart.PartState.Installed ? 3 : part.State == EnginePart.PartState.Held ? 2 : 1;
                mask |= s << (Mathf.Clamp(part.owner, 0, 1) * 2);
            }
            return mask;
        }

        void DrawMessages(float w, float h)
        {
            float a = Fade(message);
            if (a > 0f)
                UIKit.Text(new Rect(w * 0.5f - 420, h * 0.60f, 840, 56), message.text, UIKit.Style(UIKit.Face.Heavy, 22, new Color(1, 1, 1, a), TextAnchor.MiddleCenter, true));

            // instruction notification: slides in from the top, stays ~3 s, fades out
            float b = Fade(hint, 0.25f, 0.4f);
            if (b > 0f)
            {
                var r = new Rect(w * 0.5f - 320f, Slot(60) - (1f - b) * 18f, 640f, 60f);
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, b);
                MenuStyle.Glass(r);
                MenuStyle.Box(r, MenuStyle.W50, 1f);
                MenuStyle.Brackets(r);
                UIKit.Text(new Rect(r.x + 18f, r.y + 6f, r.width - 36f, r.height - 12f), hint.text, UIKit.Style(UIKit.Face.Body, 15, UIKit.Ink, TextAnchor.MiddleCenter, true));
                GUI.color = old;
            }
        }

        /// <summary>Size of the pause and camera buttons (right edge, under the power panel).</summary>
        public const float BtnSize = 60f;

        /// <summary>Pause button: right edge, under the power panel.</summary>
        void DrawPauseButton(float w, StageManager sm)
        {
            if (sm == null || sm.State != StageState.Playing) return;
            var r = new Rect(w - 18f - BtnSize, 132f, BtnSize, BtnSize);
            bool hover = r.Contains(Event.current.mousePosition);
            MenuStyle.Box(r, hover ? 1f : MenuStyle.W50, 1f);
            UIKit.Fill(new Rect(r.center.x - 10f, r.y + 18f, 6f, 24f), MenuStyle.White(1f));
            UIKit.Fill(new Rect(r.center.x + 4f, r.y + 18f, 6f, 24f), MenuStyle.White(1f));
            if (hover && Event.current.type == EventType.MouseDown) { Event.current.Use(); sm.TogglePause(); }
        }

        /// <summary>Camera switch button: right edge, under the pause button.</summary>
        void DrawCameraButton(float w, StageManager sm)
        {
            if (sm == null || sm.State != StageState.Playing) return;
            var r = new Rect(w - 18f - BtnSize, 132f + BtnSize + 12f, BtnSize, BtnSize);
            bool hover = r.Contains(Event.current.mousePosition);
            MenuStyle.Box(r, hover ? 1f : MenuStyle.W50, 1f);
            MenuStyle.Box(new Rect(r.x + 12f, r.y + 19f, 36f, 25f), MenuStyle.W80, 1f);
            MenuStyle.Box(new Rect(r.center.x - 6f, r.y + 26f, 12f, 12f), 1f, 1f, 6f);
            string view = ControlSettings.View == CameraView.Follow ? "FOLLOW" : ControlSettings.View == CameraView.ThirdPerson ? "3RD PERSON" : "SIDE VIEW";
            MenuStyle.Text(new Rect(r.xMax - 120f, r.yMax + 4f, 120f, 12f), view, UIKit.Face.Heavy, 9, MenuStyle.W50, 0.2f, TextAnchor.UpperRight);
            if (hover && Event.current.type == EventType.MouseDown)
            {
                Event.current.Use();
                StageManager.CycleCamera();
            }
        }

        void DrawHelp(float w, float h, StageManager sm)
        {
            if (sm != null && sm.ResetHold > 0f)
            {
                var rr = new Rect(w * 0.5f - 140, h * 0.5f + 40, 280, 30);
                MenuStyle.Box(rr, MenuStyle.W50, 1f);
                UIKit.Fill(new Rect(rr.x + 4, rr.y + 4, (rr.width - 8) * sm.ResetHold / sm.holdToResetSeconds, rr.height - 8), MenuStyle.White(0.35f));
                MenuStyle.Text(rr, "RESET TO CHECKPOINT", UIKit.Face.Heavy, 11, 1f, 0.25f);
            }
            if (!showHelp)
            {
                MenuStyle.Text(new Rect(16, h - 28, 400, 18), "H · show controls", UIKit.Face.Body, 12, MenuStyle.W50, 0f, TextAnchor.MiddleLeft);
                return;
            }
            var r = new Rect(14, h - 86, w - 28, 72);
            MenuStyle.Box(r, MenuStyle.W25, 1f);
            MenuStyle.Text(new Rect(r.x + 16, r.y + 8, r.width - 32, 18), controlsA, UIKit.Face.Body, 13, MenuStyle.W80, 0f, TextAnchor.MiddleLeft);
            MenuStyle.Text(new Rect(r.x + 16, r.y + 28, r.width - 32, 18), controlsB, UIKit.Face.Body, 13, MenuStyle.W80, 0f, TextAnchor.MiddleLeft);
            MenuStyle.Text(new Rect(r.x + 16, r.y + 48, r.width - 32, 16), "Hold R · reset to checkpoint      Esc · pause      H · hide controls", UIKit.Face.Body, 11, MenuStyle.W50, 0f, TextAnchor.MiddleLeft);
        }

        static Camera CamFor(PlayerController p) => p != null && p.ViewCamera != null ? p.ViewCamera : Camera.main;

        /// <summary>A marker that would sit on the instruction board is moved just below it.</summary>
        static Rect ClearOfBoard(Rect r, Camera cam)
        {
            if (!InfoBoard.ShownRect(cam, out var board) || !board.Overlaps(r)) return r;
            r.y = board.yMax + 6f;
            return r;
        }

        void DrawWorldMarkers()
        {
            float s = UIKit.Scale, w = UIKit.Width, h = UIKit.Height;
            var me = Players.Get(NetSession.Online ? NetSession.LocalPlayer : 0);
            foreach (var p in Players.All)
            {
                if (p == null) continue;
                // online every marker is drawn in this device's own view; offline in the matching split half
                var cam = NetSession.Online ? CamFor(me) : CamFor(p);
                if (cam == null || !cam.isActiveAndEnabled) continue;
                Vector3 sp = cam.WorldToScreenPoint(p.transform.position + p.Up * 1.7f);
                var gp = new Vector2(sp.x / s, (Screen.height - sp.y) / s);
                bool onScreen = sp.z > 0 && gp.x > 0 && gp.x < w && gp.y > 0 && gp.y < h;
                if (onScreen)
                {
                    var tag = ClearOfBoard(new Rect(gp.x - 13, gp.y - 34, 26, 22), cam);
                    MenuStyle.Box(tag, MenuStyle.W80, 1f, 11f);
                    MenuStyle.Text(tag, p.displayName, UIKit.Face.Display, 12, 1f);
                    if (toasts.TryGetValue(p, out var t) && Time.time < t.until)
                    {
                        float tw = MenuStyle.Measure(t.text, UIKit.Face.Heavy, 13, 0f) + 30f;
                        var tr = new Rect(gp.x - tw * 0.5f, gp.y - 72, tw, 28);
                        MenuStyle.Box(tr, MenuStyle.W50, 1f);
                        MenuStyle.Text(tr, t.text, UIKit.Face.Heavy, 13, 1f);
                    }
                }
                else
                {
                    var cpos = new Vector2(Mathf.Clamp(gp.x, 44, w - 44), Mathf.Clamp(gp.y, 150, h - 130));
                    if (sp.z < 0) cpos.y = h - 130;
                    var tag = new Rect(cpos.x - 15, cpos.y - 12, 30, 24);
                    MenuStyle.Box(tag, MenuStyle.W50, 1f, 12f);
                    MenuStyle.Text(tag, p.displayName, UIKit.Face.Display, 12, MenuStyle.W80);
                }
            }
            if (NetSession.IsClient) return; // the powers run on the host
            foreach (int i in LocalPlayers())
            {
                var pc = Players.Get(i);
                if (pc == null) continue;
                var cam = CamFor(pc);
                if (cam == null) continue;
                var np = pc.GetComponent<NullifyPower>();
                if (np != null && np.enabled && np.Active != null)
                {
                    Vector3 sp = cam.WorldToScreenPoint(np.Active.WorldBounds.center);
                    if (sp.z > 0)
                    {
                        var c = new Vector2(sp.x / s, (Screen.height - sp.y) / s);
                        MenuStyle.Arc(c, 30f, np.Active.RemainingFraction, 1f, 36, 3f);
                        MenuStyle.Text(new Rect(c.x - 30, c.y - 12, 60, 24), np.Active.Remaining.ToString("0.0"), UIKit.Face.Display, 14, 1f);
                    }
                }
                var gpw = pc.GetComponent<GravityPower>();
                if (gpw != null && gpw.enabled && gpw.Target != null)
                {
                    Vector3 sp = cam.WorldToScreenPoint(gpw.Target.transform.position);
                    if (sp.z > 0)
                    {
                        var c = new Vector2(sp.x / s, (Screen.height - sp.y) / s);
                        string label = ModeLabel(gpw.Target.Mode);
                        float tw = MenuStyle.Measure(label, UIKit.Face.Heavy, 11, 0.2f) + 24f;
                        var r = ClearOfBoard(new Rect(c.x - tw * 0.5f, c.y - 13, tw, 26), cam);
                        MenuStyle.Box(r, MenuStyle.W80, 1f, 13f);
                        MenuStyle.Text(r, label, UIKit.Face.Heavy, 11, 1f, 0.2f);
                    }
                }
            }
        }

        static readonly string[] StageHints =
        {
            "Learn to bend gravity together.",
            "Flip the station, find the engine.",
            "Carry the engine home — fast.",
            "Cross the eclipse before the light goes out.",
            "Every repair needs both powers at once."   // Stage 5
        };

        /// <summary>
        /// The stage instructions card (final design): a transparent double-hairline box with the stage, its hint,
        /// difficulty and keys on the left, mode / objective / players on the right, the story below, a waving
        /// astronaut in the corner and "Ready when you are" underneath. Tap or Space to start.
        /// </summary>
        void DrawIntro(float w, float h, StageManager sm)
        {
            float bw = Mathf.Min(w * 0.62f, 1180f), bh = h * 0.66f;
            var box = new Rect((w - bw) * 0.5f, h * 0.05f, bw, bh);
            MenuStyle.Box(box, MenuStyle.W50, 1f);
            MenuStyle.Box(new Rect(box.x + 5f, box.y + 5f, box.width - 10f, box.height - 10f), MenuStyle.W25, 1f);
            // the small arc ornament on the bottom edge
            GUI.BeginGroup(new Rect(box.x + bw * 0.195f, box.yMax - 26f, 58f, 22f));
            MenuStyle.Box(new Rect(0f, 0f, 116f, 116f), MenuStyle.W50, 1f, 58f);
            GUI.EndGroup();

            int n = Mathf.Clamp(sm.stageNumber, 1, StageHints.Length);
            MenuStyle.ShadowAlpha = 0.8f; // transparent card over the live game: stronger shadow for legibility
            string sub = sm.stageTitle.Contains("—") ? sm.stageTitle.Substring(sm.stageTitle.IndexOf('—') + 1).Trim() : sm.stageTitle;
            float lx = box.x + bw * 0.06f, lw = bw * 0.5f, y = box.y + 34f;
            MenuStyle.Text(new Rect(lx, y, lw, 12f), "STAGE", UIKit.Face.Heavy, 10, MenuStyle.W50, 0.3f, TextAnchor.UpperLeft);
            y += 18f;
            MenuStyle.Text(new Rect(lx, y, lw, 44f), (n + " · " + sub).ToUpperInvariant(), UIKit.Face.Display, 30, 1f, 0.02f, TextAnchor.UpperLeft);
            y += 50f;
            MenuStyle.Text(new Rect(lx, y, lw, 18f), StageHints[n - 1], UIKit.Face.Body, 14, MenuStyle.W80, 0f, TextAnchor.UpperLeft);
            y += 30f;
            MenuStyle.Text(new Rect(lx, y, 100f, 12f), "DIFFICULTY", UIKit.Face.Heavy, 10, MenuStyle.W50, 0.3f, TextAnchor.UpperLeft);
            float px = lx + MenuStyle.Measure("DIFFICULTY", UIKit.Face.Heavy, 10, 0.3f) + 12f;
            for (int p = 1; p <= StageHints.Length; p++)
            {
                var pip = new Rect(px + (p - 1) * 27f, y + 5f, 22f, 3f);
                if (p <= n) UIKit.Fill(pip, MenuStyle.White(1f));
                else MenuStyle.Box(pip, MenuStyle.W50, 1f);
            }
            y += 30f;
            float kx = lx;
            var keyHints = GameFlow.Solo
                ? new[] { (new[] { "WASD", "Arrows" }, "move"), (new[] { "Space" }, "jump"), (new[] { "E" }, "gravity"), (new[] { "Q" }, "freeze"), (new[] { "Mouse" }, "look") }
                : new[] { (new[] { "W", "A", "S", "D" }, "move"), (new[] { "Space" }, "jump"), (new[] { "E" }, "power"), (new[] { "Mouse" }, "look") };
            foreach (var (keys, label) in keyHints)
            {
                foreach (var k in keys) kx += MenuStyle.Key(kx, y + 10f, k) + 4f;
                float tw = MenuStyle.Measure(label, UIKit.Face.Body, 12, 0f);
                MenuStyle.Text(new Rect(kx, y + 1f, tw + 4f, 18f), label, UIKit.Face.Body, 12, MenuStyle.W50, 0f, TextAnchor.MiddleLeft);
                kx += tw + 18f;
            }
            y += 40f;
            // the story, in the open space under the keys
            UIKit.Text(new Rect(lx, y, box.xMax - lx - 40f, box.yMax - y - 30f), sm.introText, UIKit.Style(UIKit.Face.Body, 15, MenuStyle.White(0.9f), TextAnchor.UpperLeft, true));

            // right column: label / value rows with dashed rules
            float rx = box.x + bw * 0.62f, rw = bw * 0.33f, ry = box.y + 34f;
            int count = GameFlow.PlayerCount;
            string modeText = NetSession.Online ? (NetSession.IsHost ? "Online · Host" : "Online · Player " + GameFlow.Letter(NetSession.LocalPlayer)) : GameFlow.Solo ? "Solo" : "Local · Shared screen";
            string playersText = GameFlow.Solo ? "1 · Both powers" : count + " · Co-op";
            foreach (var (label, value) in new[] { ("MODE", modeText), ("OBJECTIVE", sm.objectiveText), ("PLAYERS", playersText) })
            {
                MenuStyle.Text(new Rect(rx, ry, rw, 18f), label, UIKit.Face.Heavy, 10, MenuStyle.W50, 0.3f, TextAnchor.MiddleLeft);
                MenuStyle.Text(new Rect(rx, ry, rw, 18f), value, UIKit.Face.Body, 13, MenuStyle.W80, 0f, TextAnchor.MiddleRight);
                MenuStyle.DashedLine(rx, rx + rw, ry + 26f, MenuStyle.W25);
                ry += 35f;
            }

            MenuStyle.Text(new Rect(0, box.yMax + 46f, w, 22f), GameFlow.Solo ? "Ready when you are. Bend it, freeze it, and don't let go." : "Ready when you are. Stay close to your crew, and don't let go.", UIKit.Face.Body, 17, 1f, 0f, TextAnchor.UpperCenter);
            float pulse = 0.45f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);
            MenuStyle.Text(new Rect(0, box.yMax + 82f, w, 14f), "STARTING IN " + Mathf.CeilToInt(sm.IntroLeft) + "  ·  TAP OR PRESS SPACE TO START NOW", UIKit.Face.Heavy, 11, pulse, 0.3f, TextAnchor.UpperCenter);

            MenuStyle.Astronaut(new Vector2(150f, h - 18f), 1f);
            MenuStyle.ShadowAlpha = 0.35f;
        }

        // the mission banner when a stage starts: what to do, in one line
        static readonly (string title, string line)[] Missions =
        {
            ("REACH THE STATION", "Cross the shattered orbit and step into the beacon at the station airlock."),
            ("FIND THE ENGINE VAULT", "Make it through the upside-down station — the vault waits at the far end."),
            ("GET BACK TO THE SHIP", "The station is falling apart. Carry the engine home before it's gone."),
            ("CROSS THE EVENT HORIZON", "The eclipse is bending gravity. Carry each other through before the light goes out."),
            ("REPAIR THE SHIP", "One bends gravity, the other freezes the machinery. Finish every repair together."),   // Stage 5
        };

        void DrawMission(float w, float h, StageManager sm)
        {
            int n = Mathf.Clamp(sm.stageNumber, 1, Missions.Length);
            float left = sm.MissionSecondsLeft;
            // fade in over the first 0.3 s and out over the last 0.4 s
            float a = Mathf.Clamp01((StageManager.MissionSeconds - left) / 0.3f) * Mathf.Clamp01(left / 0.4f);
            float cy = h * 0.5f;
            MenuStyle.ShadowAlpha = 0.8f;
            MenuStyle.Text(new Rect(0, cy - 92f, w, 14f), "MISSION  ·  STAGE " + n, UIKit.Face.Heavy, 12, MenuStyle.W80 * a, 0.4f, TextAnchor.UpperCenter);
            MenuStyle.Line(w * 0.5f - 160f, cy - 66f, 320f, MenuStyle.W50 * a);
            MenuStyle.Text(new Rect(0, cy - 52f, w, 64f), Missions[n - 1].title, UIKit.Face.Display, 52, a, 0.08f, TextAnchor.UpperCenter);
            MenuStyle.Text(new Rect(0, cy + 22f, w, 24f), Missions[n - 1].line, UIKit.Face.Body, 18, 0.9f * a, 0f, TextAnchor.UpperCenter);
            // a thin bar that runs out with the banner
            float bw = 240f * Mathf.Clamp01(left / StageManager.MissionSeconds);
            UIKit.Fill(new Rect(w * 0.5f - 120f, cy + 70f, 240f, 2f), MenuStyle.White(MenuStyle.W25 * a));
            UIKit.Fill(new Rect(w * 0.5f - 120f, cy + 70f, bw, 2f), MenuStyle.White(a));
            MenuStyle.ShadowAlpha = 0.35f;
        }

        /// <summary>The settings screen is open over the pause menu (on this device only).</summary>
        public static bool SettingsOpen;

        void DrawPause(float w, float h, StageManager sm)
        {
            MenuStyle.ShadowAlpha = 0.8f; // transparent over the live game
            if (SettingsOpen)
            {
                if (SettingsPanel.Draw(w, h)) SettingsOpen = false;
                MenuStyle.ShadowAlpha = 0.35f;
                return;
            }
            // compact list, centred on the screen
            const float rowW = 340f, rowH = 44f, step = 52f;
            float top = h * 0.5f - (StageManager.PauseItems.Length * step) * 0.5f + 20f;
            MenuStyle.Text(new Rect(0, top - 60f, w, 40f), "PAUSED", UIKit.Face.Display, 32, 1f, 0.3f);
            for (int i = 0; i < StageManager.PauseItems.Length; i++)
            {
                var r = new Rect(w * 0.5f - rowW * 0.5f, top + i * step, rowW, rowH);
                bool active = sm.PauseSelection == i, hover = r.Contains(Event.current.mousePosition);
                MenuStyle.Box(r, active ? MenuStyle.W50 : hover ? MenuStyle.W25 : MenuStyle.W12, 1f);
                if (active) MenuStyle.Brackets(r);
                MenuStyle.Text(r, StageManager.PauseLabel(i).ToUpperInvariant(), UIKit.Face.Heavy, 15, 1f, 0.22f);
                if (hover && Event.current.type == EventType.MouseDown) { Event.current.Use(); sm.PauseSelection = i; sm.ChoosePauseItem(i); }
            }
            MenuStyle.Text(new Rect(0, top + StageManager.PauseItems.Length * step + 8f, w, 16), "W / S choose  ·  Space / Enter confirm  ·  Esc resume",
                UIKit.Face.Body, 12, MenuStyle.W50);
            MenuStyle.ShadowAlpha = 0.35f;
        }

        void DrawComplete(float w, float h, StageManager sm)
        {
            MenuStyle.ShadowAlpha = 0.8f; // transparent over the live game
            var box = new Rect(w * 0.5f - 400, h * 0.18f, 800, 330);
            MenuStyle.Box(box, MenuStyle.W50, 1f);
            MenuStyle.Box(new Rect(box.x + 5, box.y + 5, box.width - 10, box.height - 10), MenuStyle.W25, 1f);
            MenuStyle.Text(new Rect(box.x, box.y + 34, box.width, 12), "STAGE " + sm.stageNumber + " · COMPLETE", UIKit.Face.Heavy, 10, MenuStyle.W50, 0.3f, TextAnchor.UpperCenter);
            MenuStyle.Text(new Rect(box.x, box.y + 54, box.width, 48), sm.completeTitle.ToUpperInvariant(), UIKit.Face.Display, 38, 1f, 0.06f, TextAnchor.UpperCenter);
            UIKit.Text(new Rect(box.x + 60, box.y + 118, box.width - 120, 120), sm.completeText, UIKit.Style(UIKit.Face.Body, 17, MenuStyle.White(0.9f), TextAnchor.UpperCenter, true));
            int m = Mathf.FloorToInt(sm.StageTime / 60f), sec = Mathf.FloorToInt(sm.StageTime % 60f);
            MenuStyle.DashedLine(box.x + 60, box.xMax - 60, box.yMax - 70, MenuStyle.W25);
            MenuStyle.Text(new Rect(box.x, box.yMax - 56, box.width, 18), $"TIME  {m}:{sec:00}      ·      RESPAWNS  {sm.Deaths}", UIKit.Face.Heavy, 12, MenuStyle.W80, 0.2f, TextAnchor.UpperCenter);
            bool last = sm.isFinalStage || !GameFlow.HasNext(sm.stageNumber);
            int left = Mathf.CeilToInt(sm.AutoContinueLeft);
            string next = last ? "Heading home" : "Next: Stage " + (sm.stageNumber + 1);
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);
            MenuStyle.Text(new Rect(0, box.yMax + 30, w, 16), (next + (left > 0 ? "  in " + left + "…" : "…")).ToUpperInvariant(), UIKit.Face.Heavy, 13, pulse, 0.3f, TextAnchor.UpperCenter);
            MenuStyle.Text(new Rect(0, box.yMax + 56, w, 14), "Tap or press Space to go now", UIKit.Face.Body, 12, MenuStyle.W50, 0f, TextAnchor.UpperCenter);
            MenuStyle.ShadowAlpha = 0.35f;
        }
    }
}