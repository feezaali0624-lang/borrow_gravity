using UnityEngine;
using UnityEngine.InputSystem;

namespace BorrowGravity
{
    /// <summary>
    /// Title screen in the final menu design (borrowed-gravity-menu.html): white-only outlines over the orbiting scene.
    /// Brand on the left; on the right the stage selector (with difficulty pips),
    /// START, Controls / Settings / Quit and a key-hint footer. START asks how to play (solo, host a room
    /// for two, join). Overlays: Play chooser, Controls, Settings, Quit confirm, Launching.
    /// Mode = play solo (both powers), host a room for two players, or join one (every player on their own device,
    /// full screen). Keyboard, mouse or touch.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public HudIcons icons;
        public Transform orbitTarget;
        public float orbitSpeed = 4f;
        public float orbitRadius = 34f;
        public float orbitHeight = 10f;
        [Tooltip("How soft the orbiting scene behind the menu is (0 = sharp, 1 = very blurred).")]
        [Range(0f, 1f)] public float backgroundBlur = 0.5f;

        enum Row { Stage, Start, Controls, Settings, Quit }
        static readonly Row[] Rows = { Row.Stage, Row.Start, Row.Controls, Row.Settings, Row.Quit };
        enum Overlay { None, Play, Controls, Quit, Launch, Settings }

        static readonly (string name, string hint, int diff)[] Stages =
        {
            ("1 · Shattered Orbit", "Learn to bend gravity together.", 1),
            ("2 · Upside-Down Station", "Flip the station, find the engine.", 2),
            ("3 · Collapsing Return", "Carry the engine home — fast.", 3),
            ("4 · Event Horizon", "Cross the eclipse before the light goes out.", 4),
            ("5 · Final Repair", "Every repair needs both powers at once.", 5),
        };
        // 0 solo, 1 host a room for two players, 2 join
        static readonly (string name, string hint)[] Modes =
        {
            ("Solo", "Play alone with both powers."),
            ("2 Players", "Create a room · share the code."),
            ("Join a Room", "Type the code from the host's screen."),
        };
        const int HostMode = 1, JoinMode = 2;
        bool Solo => mode == 0;
        bool Joining => mode == JoinMode;
        // digits + letters (internet room codes look like K7Q2XD), '.' for IPs, '<' = delete
        static readonly string[] Keypad =
        {
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "0",
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J",
            "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T",
            "U", "V", "W", "X", "Y", "Z", ".", "<"
        };

        int row = (int)Row.Start;
        int stage;
        int mode = 1;
        bool cutscene = true;
        Overlay overlay;
        bool joinEntry;
        string joinCode = "";
        bool autoStart;
        float angle, introFade = 1f, launchStart;
        readonly float[] changedAt = new float[Rows.Length];
        Vector2 lastMouse;

        void Awake() => useGUILayout = false; // no GUILayout here: skip IMGUI's extra layout pass every frame

        void Start()
        {
            Time.timeScale = 1f;
            if (NetSession.Online) mode = NetSession.IsHost ? HostMode : JoinMode;
            else DevCommandLine();
            for (int i = 0; i < changedAt.Length; i++) changedAt[i] = -10f;
            Sfx.SetLoop(LoopId.Ambience, true, 0.35f);
            Sfx.SetLoop(LoopId.Music, true, 0.8f);
        }

        // ---------- soft background: the orbit camera renders small, it's blurred and drawn behind the menu ----------

        Camera sceneCam;
        RenderTexture sceneTex, softTex;

        void SetupBlur()
        {
            if (backgroundBlur <= 0f || sceneCam != null) return;
            sceneCam = Camera.main;
            if (sceneCam == null) return;
            int w = Mathf.Max(64, Screen.width / 2), h = Mathf.Max(64, Screen.height / 2);
            sceneTex = new RenderTexture(w, h, 24) { name = "MenuScene" };
            sceneCam.targetTexture = sceneTex;
            // something still has to clear the screen behind the GUI
            var clear = new GameObject("MenuScreenClear").AddComponent<Camera>();
            clear.cullingMask = 0;
            clear.clearFlags = CameraClearFlags.SolidColor;
            clear.backgroundColor = Color.black;
            clear.depth = sceneCam.depth - 1f;
        }

        void BlurScene()
        {
            if (sceneTex == null) return;
            // each bilinear halving averages 2x2 pixels: more halvings = softer
            int steps = Mathf.Clamp(Mathf.RoundToInt(backgroundBlur * 4f), 1, 4);
            int w = sceneTex.width, h = sceneTex.height;
            var cur = sceneTex;
            var temps = new System.Collections.Generic.List<RenderTexture>();
            for (int i = 0; i < steps; i++)
            {
                w = Mathf.Max(8, w / 2); h = Mathf.Max(8, h / 2);
                var next = RenderTexture.GetTemporary(w, h, 0);
                Graphics.Blit(cur, next);
                temps.Add(next);
                cur = next;
            }
            for (int i = 0; i < steps - 1; i++)
            {
                w *= 2; h *= 2;
                var next = RenderTexture.GetTemporary(w, h, 0);
                Graphics.Blit(cur, next);
                temps.Add(next);
                cur = next;
            }
            if (softTex == null || softTex.width != w || softTex.height != h)
            {
                if (softTex != null) { softTex.Release(); Destroy(softTex); }
                softTex = new RenderTexture(w, h, 0) { filterMode = FilterMode.Bilinear, name = "MenuSoft" };
            }
            Graphics.Blit(cur, softTex);
            foreach (var t in temps) RenderTexture.ReleaseTemporary(t);
            RenderTexture.active = null;
        }

        // last frame's render, blurred before this frame's GUI (blitting inside OnGUI would steal its target)
        void LateUpdate() => BlurScene();

        void OnDestroy()
        {
            if (sceneCam != null) sceneCam.targetTexture = null;
            if (sceneTex != null) { sceneTex.Release(); Destroy(sceneTex); }
            if (softTex != null) { softTex.Release(); Destroy(softTex); }
        }

        // ---------- splash: the title on black once per launch, before the menu ----------

        const float SplashSeconds = 3.2f;
        static bool splashDone;
        float splashStart = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSplash() => splashDone = false;

        bool SplashShowing => !splashDone && !NetSession.Online;

        /// <summary>Runs the splash; true while it is up (the menu takes no input then). Any key / tap skips it.</summary>
        bool UpdateSplash()
        {
            if (!SplashShowing) return false;
            if (splashStart < 0f) splashStart = Time.unscaledTime;
            float t = Time.unscaledTime - splashStart;
            bool skip = t > 0.4f && ((Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                        || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                        || (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame));
            if (t >= SplashSeconds || skip) { splashDone = true; introFade = 1f; return false; }
            return true;
        }

        void DrawSplash(float w, float h)
        {
            float t = Time.unscaledTime - splashStart;
            float a = Mathf.Clamp01(t / 0.8f) * Mathf.Clamp01((SplashSeconds - t) / 0.6f);
            UIKit.Fill(new Rect(0, 0, w, h), Color.black);
            int size = (int)Mathf.Clamp(w * 0.07f, 40f, 96f);
            const string first = "BORROWED ", second = "GRAVITY";
            float wa = MenuStyle.Measure(first, UIKit.Face.Body, size, 0.02f), wb = MenuStyle.Measure(second, UIKit.Face.Display, size, 0.02f);
            float x = (w - wa - wb) * 0.5f, y = h * 0.5f - size * 0.75f;
            MenuStyle.Text(new Rect(x, y, wa + 4f, size * 1.1f), first, UIKit.Face.Body, size, a, 0.02f, TextAnchor.UpperLeft);
            MenuStyle.Text(new Rect(x + wa, y, wb + 4f, size * 1.1f), second, UIKit.Face.Display, size, a, 0.02f, TextAnchor.UpperLeft);
            MenuStyle.Text(new Rect(0, y + size * 1.1f + 16f, w, 18f), "A 2-PLAYER CO-OP GRAVITY ADVENTURE", UIKit.Face.Heavy, 13, a * MenuStyle.W50, 0.22f);
        }

        void Update()
        {
            if (UpdateSplash()) return;
            SetupBlur();
            var cam = Camera.main;
            if (cam != null && orbitTarget != null)
            {
                angle += orbitSpeed * Time.deltaTime;
                float a = angle * Mathf.Deg2Rad;
                cam.transform.position = orbitTarget.position + new Vector3(Mathf.Sin(a) * orbitRadius, orbitHeight, -Mathf.Cos(a) * orbitRadius);
                cam.transform.LookAt(orbitTarget.position + Vector3.up * 2f);
                // frame the set on the left (under the title) so the menu column on the right sits over open sky
                cam.transform.position += cam.transform.right * 9f + cam.transform.up * 1.5f;
            }
            introFade = Mathf.MoveTowards(introFade, 0f, Time.deltaTime);

            if (overlay == Overlay.Launch)
            {
                if (Time.unscaledTime - launchStart > 2.2f) { overlay = Overlay.None; StartGameNow(); }
                return;
            }
            if (RoomPanelOpen) { RoomKeys(); return; }
            if (overlay == Overlay.Settings)
            {
                if (SettingsPanel.HandleKeys()) CloseOverlay();
                return;
            }
            if (overlay == Overlay.Play)
            {
                if (PlayerKeys.Pressed(Key.Escape)) CloseOverlay();
                if (PlayerKeys.Pressed(Key.W) || PlayerKeys.Pressed(Key.UpArrow)) { mode = (mode + Modes.Length - 1) % Modes.Length; Sfx.Play(SfxId.Tick, 0.5f); }
                if (PlayerKeys.Pressed(Key.S) || PlayerKeys.Pressed(Key.DownArrow)) { mode = (mode + 1) % Modes.Length; Sfx.Play(SfxId.Tick, 0.5f); }
                if (Confirm()) Play(mode);
                return;
            }
            if (overlay != Overlay.None)
            {
                if (PlayerKeys.Pressed(Key.Escape)) CloseOverlay();
                else if (Confirm()) CloseOverlay(); // Back / Stay are the focused buttons
                return;
            }
            if (PlayerKeys.Pressed(Key.W) || PlayerKeys.Pressed(Key.UpArrow)) Move(-1);
            if (PlayerKeys.Pressed(Key.S) || PlayerKeys.Pressed(Key.DownArrow)) Move(1);
            if (PlayerKeys.Pressed(Key.A) || PlayerKeys.Pressed(Key.LeftArrow)) Change(-1);
            if (PlayerKeys.Pressed(Key.D) || PlayerKeys.Pressed(Key.RightArrow)) Change(1);
            if (Confirm()) Activate(Rows[row]);
        }

        static bool Confirm() => PlayerKeys.Pressed(Key.Enter) || PlayerKeys.Pressed(Key.Space) || PlayerKeys.Pressed(Key.NumpadEnter);

        void Move(int d)
        {
            row = (row + d + Rows.Length) % Rows.Length;
            Sfx.Play(SfxId.Tick, 0.5f);
        }

        void Change(int d)
        {
            switch (Rows[row])
            {
                case Row.Stage: stage = (stage + d + GameFlow.Unlocked) % GameFlow.Unlocked; break;
                default: return;
            }
            changedAt[row] = Time.unscaledTime;
            Sfx.Play(SfxId.Tick, 0.6f, 1.2f);
        }

        void Activate(Row r)
        {
            Sfx.Play(SfxId.Plate, 0.6f);
            switch (r)
            {
                case Row.Start: overlay = Overlay.Play; break;
                case Row.Controls: overlay = Overlay.Controls; break;
                case Row.Settings: overlay = Overlay.Settings; break;
                case Row.Quit: overlay = Overlay.Quit; break;
                default: Change(1); break;
            }
        }

        /// <summary>A choice from the START screen: solo, a room for two, or join one.</summary>
        void Play(int choice)
        {
            mode = choice;
            overlay = Overlay.None;
            NetSession.LastError = "";
            Sfx.Play(SfxId.Plate, 0.6f);
            if (Solo) { GameFlow.PlayerCount = 1; StartGame(); }
            else if (Joining) joinEntry = true;
            else { GameFlow.PlayerCount = GameFlow.MaxPlayers; NetSession.HostRoom(); }
        }

        void CloseOverlay()
        {
            overlay = Overlay.None;
            Sfx.Play(SfxId.Plate, 0.5f);
        }

        void Quit()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // ---------- drawing ----------

        void OnGUI()
        {
            UIKit.Begin();
            float w = UIKit.Width, h = UIKit.Height;
            if (SplashShowing && splashStart >= 0f) { DrawSplash(w, h); return; }
            if (softTex != null && Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(0, 0, w, h), softTex, ScaleMode.StretchToFill);
                UIKit.Fill(new Rect(0, 0, w, h), new Color(0.02f, 0.03f, 0.07f, 0.45f)); // darker, so the white menu stands out
            }
            // fully transparent: white lines and text straight over the orbiting scene (text has a soft shadow)

            bool menuLive = overlay == Overlay.None && !RoomPanelOpen;
            float pad = Mathf.Clamp(w * 0.05f, 20f, 72f), gap = Mathf.Clamp(w * 0.04f, 16f, 64f);
            float avail = w - pad * 2f - gap;
            float leftW = avail * 1.15f / 2.15f, rightW = avail / 2.15f;

            if (overlay != Overlay.Settings) DrawBrand(pad, pad + 28f, leftW); // the tall settings box would cover it
            if (menuLive) DrawPanel(w - pad - Mathf.Min(520f, rightW), Mathf.Min(520f, rightW), h, true);

            if (!string.IsNullOrEmpty(NetSession.LastError) && !RoomPanelOpen)
                MenuStyle.Text(new Rect(0, h - 44, w, 24), NetSession.LastError, UIKit.Face.Heavy, 14, MenuStyle.W80);

            if (RoomPanelOpen) DrawRoom(w, h);
            else if (overlay == Overlay.Play) DrawPlay(w, h);
            else if (overlay == Overlay.Controls) DrawControls(w, h);
            else if (overlay == Overlay.Quit) DrawQuit(w, h);
            else if (overlay == Overlay.Launch) DrawLaunch(w, h);
            else if (overlay == Overlay.Settings) { if (SettingsPanel.Draw(w, h)) CloseOverlay(); }
            if (introFade > 0f) UIKit.Fill(new Rect(0, 0, w, h), new Color(0, 0, 0, introFade));
        }

        void DrawBrand(float x, float y, float width)
        {
            int size = (int)Mathf.Clamp(UIKit.Width * 0.06f, 40f, 84f);
            const string a = "BORROWED ", b = "GRAVITY";
            float wa = MenuStyle.Measure(a, UIKit.Face.Body, size, 0.02f);
            MenuStyle.Text(new Rect(x, y, width, size * 1.1f), a, UIKit.Face.Body, size, 1f, 0.02f, TextAnchor.UpperLeft);
            MenuStyle.Text(new Rect(x + wa + size * 0.02f, y, width, size * 1.1f), b, UIKit.Face.Display, size, 1f, 0.02f, TextAnchor.UpperLeft);
            MenuStyle.Text(new Rect(x, y + size * 1.1f + 14f, width, 18f), "A 2-PLAYER CO-OP GRAVITY ADVENTURE", UIKit.Face.Heavy, 13, MenuStyle.W50, 0.22f, TextAnchor.UpperLeft);
        }

        void DrawPanel(float x, float width, float h, bool live)
        {
            var e = Event.current;
            Vector2 mouse = e.mousePosition;
            bool mouseMoved = (mouse - lastMouse).sqrMagnitude > 0.5f && e.type == EventType.Repaint;
            if (e.type == EventType.Repaint) lastMouse = mouse;

            // heights from the design: rows 12 padding, start 18, secondary 14
            float[] heights = { 96f, 63f, 45f };
            float total = heights[0] + 8f + 14f + heights[1] + 8f + heights[2] + 18f + 14f + 22f;
            float y = Mathf.Max(24f, (h - total) * 0.5f);

            DrawSelectRow(0, new Rect(x, y, width, heights[0]), live, mouseMoved);
            y += heights[0] + 8f;

            // START
            y += 14f - 8f + 8f;
            var start = new Rect(x, y, width, heights[1]);
            RowFrame(start, (int)Row.Start, true, live, mouseMoved);
            MenuStyle.Text(start, "START", UIKit.Face.Heavy, 22, 1f, 0.4f);
            y += heights[1] + 8f;

            // secondary: Controls | Settings | Quit
            float third = (width - 16f) / 3f;
            var ctl = new Rect(x, y, third, heights[2]);
            var set = new Rect(x + third + 8f, y, third, heights[2]);
            var quit = new Rect(x + (third + 8f) * 2f, y, third, heights[2]);
            RowFrame(ctl, (int)Row.Controls, false, live, mouseMoved);
            RowFrame(set, (int)Row.Settings, false, live, mouseMoved);
            RowFrame(quit, (int)Row.Quit, false, live, mouseMoved);
            MenuStyle.Text(ctl, "CONTROLS", UIKit.Face.Heavy, 13, 1f, 0.22f);
            MenuStyle.Text(set, "SETTINGS", UIKit.Face.Heavy, 13, 1f, 0.22f);
            MenuStyle.Text(quit, "QUIT", UIKit.Face.Heavy, 13, 1f, 0.22f);
            y += heights[2] + 18f;

            // key hints
            MenuStyle.Line(x, y, width, MenuStyle.W25);
            y += 14f + 11f;
            DrawKeyHints(x, width, y);
        }

        /// <summary>Row outline: hover/active border, corner brackets when active; hover selects, click confirms.</summary>
        void RowFrame(Rect r, int index, bool isStart, bool live, bool mouseMoved)
        {
            var e = Event.current;
            bool hover = live && r.Contains(e.mousePosition);
            if (hover && mouseMoved && row != index) { row = index; Sfx.Play(SfxId.Tick, 0.25f); }
            bool active = row == index;
            float border = isStart ? MenuStyle.W50 : active ? MenuStyle.W25 : hover ? MenuStyle.W12 : 0f;
            if (isStart && active) border = MenuStyle.W50;
            MenuStyle.Box(r, border, 1f);
            if (active) MenuStyle.Brackets(r);
            if (live && hover && e.type == EventType.MouseDown)
            {
                e.Use();
                row = index;
                Activate(Rows[index]);
            }
        }

        void DrawSelectRow(int index, Rect r, bool live, bool mouseMoved)
        {
            var e = Event.current;
            // arrows first, so their clicks win over the row's click
            var left = new Rect(r.x + 12f, r.center.y - 16f, 32f, 32f);
            var right = new Rect(r.xMax - 44f, r.center.y - 16f, 32f, 32f);
            foreach (var (box, d) in new[] { (left, -1), (right, 1) })
            {
                bool over = live && box.Contains(e.mousePosition);
                if (over) UIKit.Fill(box, MenuStyle.White(MenuStyle.W12));
                MenuStyle.Box(box, MenuStyle.W50, 1f);
                MenuStyle.Text(box, d < 0 ? "‹" : "›", UIKit.Face.Heavy, 18, 1f, 0f, TextAnchor.MiddleCenter, -1f);
                if (over && e.type == EventType.MouseDown) { e.Use(); row = index; Change(d); }
            }
            RowFrame(r, index, false, live, mouseMoved);

            var mid = new Rect(left.xMax + 10f, r.y + 12f, right.x - left.xMax - 20f, r.height - 24f);
            const string label = "STAGE";
            MenuStyle.Text(new Rect(mid.x, mid.y, mid.width, 12f), label, UIKit.Face.Heavy, 10, MenuStyle.W50, 0.3f, TextAnchor.UpperCenter);

            // the value slides in when it changes
            float k = Mathf.Clamp01((Time.unscaledTime - changedAt[index]) / 0.2f);
            k = 1f - (1f - k) * (1f - k);
            string value = Stages[stage].name;
            MenuStyle.Text(new Rect(mid.x, mid.y + 16f, mid.width, 24f), value.ToUpperInvariant(), UIKit.Face.Heavy, 18, k, 0.08f, TextAnchor.UpperCenter, 5f * (1f - k));

            float ey = mid.y + 16f + 24f + 5f;
            MenuStyle.Text(new Rect(mid.x, ey, mid.width, 16f), Stages[stage].hint, UIKit.Face.Body, 12, MenuStyle.W50, 0f, TextAnchor.UpperCenter);
            int pips = Stages.Length;
            float px = mid.center.x - (pips * 18f + (pips - 1) * 4f) * 0.5f;
            for (int p = 1; p <= pips; p++)
            {
                var pip = new Rect(px + (p - 1) * 22f, ey + 16f + 7f, 18f, 3f);
                if (p <= Stages[stage].diff) UIKit.Fill(pip, MenuStyle.White(1f));
                else MenuStyle.Box(pip, MenuStyle.W50, 1f);
            }
        }

        void DrawKeyHints(float x, float width, float cy)
        {
            // measure the three groups, then centre them with 16px between
            float Group(float gx, string[] keys, string label, bool draw)
            {
                float cx = gx;
                foreach (var k in keys)
                {
                    if (draw) cx += MenuStyle.Key(cx, cy, k) + 4f;
                    else cx += Mathf.Max(24f, MenuStyle.Measure(k, UIKit.Face.Heavy, 11, 0f) + 14f) + 4f;
                }
                float lw = MenuStyle.Measure(label, UIKit.Face.Body, 12, 0f);
                if (draw) MenuStyle.Text(new Rect(cx + 2f, cy - 9f, lw + 4f, 18f), label, UIKit.Face.Body, 12, MenuStyle.W50, 0f, TextAnchor.MiddleLeft);
                return cx + 2f + lw - gx;
            }
            var groups = new (string[] keys, string label)[]
            {
                (new[] { "W", "S" }, "choose"), (new[] { "A", "D" }, "change"), (new[] { "Space", "Enter" }, "confirm")
            };
            float total = 0f;
            foreach (var g in groups) total += Group(0f, g.keys, g.label, false);
            total += 16f * (groups.Length - 1);
            float gx2 = x + (width - total) * 0.5f;
            foreach (var g in groups) gx2 += Group(gx2, g.keys, g.label, true) + 16f;
        }

        // ---------- overlays ----------

        static Rect Modal(float w, float h, float width, float height)
        {
            var r = new Rect((w - width) * 0.5f, (h - height) * 0.5f, width, height);
            MenuStyle.Box(r, 1f, 1f);
            return r;
        }

        /// <summary>START: how do you want to play? One row per choice; the seats show how many players.</summary>
        void DrawPlay(float w, float h)
        {
            var e = Event.current;
            const float rowH = 58f, gap = 8f;
            var r = Modal(w, h, Mathf.Min(w * 0.92f, 560f), 26f + 22f + 24f + Modes.Length * (rowH + gap) + 78f);
            float x = r.x + 26f, inner = r.width - 52f, y = r.y + 26f;
            MenuStyle.Text(new Rect(x, y, inner, 22f), "HOW DO YOU WANT TO PLAY?", UIKit.Face.Display, 18, 1f, 0.2f, TextAnchor.UpperLeft);
            y += 22f + 24f;
            for (int i = 0; i < Modes.Length; i++)
            {
                var row = new Rect(x, y, inner, rowH);
                bool hover = row.Contains(e.mousePosition), active = mode == i;
                if (hover && e.type == EventType.MouseMove && mode != i) { mode = i; Sfx.Play(SfxId.Tick, 0.25f); }
                MenuStyle.Box(row, active ? MenuStyle.W50 : hover ? MenuStyle.W25 : MenuStyle.W12, 1f);
                if (active) MenuStyle.Brackets(row);
                MenuStyle.Text(new Rect(row.x + 18f, row.y + 10f, inner * 0.6f, 22f), Modes[i].name.ToUpperInvariant(), UIKit.Face.Heavy, 16, 1f, 0.12f, TextAnchor.UpperLeft);
                MenuStyle.Text(new Rect(row.x + 18f, row.y + 33f, inner * 0.65f, 16f), Modes[i].hint, UIKit.Face.Body, 12, MenuStyle.W50, 0f, TextAnchor.UpperLeft);
                // seats: one small box per player (A, B), or "+" for joining someone else's room
                int seats = i == JoinMode ? 0 : i + 1;
                float sx = row.xMax - 18f;
                if (seats == 0) MenuStyle.Text(new Rect(sx - 60f, row.y, 60f, rowH), "CODE ›", UIKit.Face.Heavy, 11, MenuStyle.W80, 0.2f, TextAnchor.MiddleRight);
                for (int p = seats - 1; p >= 0; p--)
                {
                    var seat = new Rect(sx - 26f, row.center.y - 13f, 26f, 26f);
                    MenuStyle.Box(seat, active ? 1f : MenuStyle.W50, 1f);
                    MenuStyle.Text(seat, GameFlow.Letter(p), UIKit.Face.Display, 12, active ? 1f : MenuStyle.W80, 0f);
                    sx -= 32f;
                }
                if (hover && e.type == EventType.MouseDown) { e.Use(); Play(i); }
                y += rowH + gap;
            }
            if (MenuStyle.Button(new Rect(r.xMax - 26f - 96f, r.yMax - 26f - 34f, 96f, 34f), "Back")) CloseOverlay();
            MenuStyle.Text(new Rect(x, r.yMax - 26f - 34f, inner - 110f, 34f), "W / S choose  ·  Enter play  ·  Esc back", UIKit.Face.Body, 12, MenuStyle.W50, 0f, TextAnchor.MiddleLeft);
        }

        void DrawControls(float w, float h)
        {
            float mw = Mathf.Min(w * 0.92f, 640f);
            var r = Modal(w, h, mw, 360f);
            float x = r.x + 26f, y = r.y + 26f, inner = r.width - 52f;
            MenuStyle.Text(new Rect(x, y, inner, 22f), "CONTROLS", UIKit.Face.Display, 18, 1f, 0.3f, TextAnchor.UpperLeft);
            y += 22f + 16f;
            float colW = (inner - 16f) * 0.5f;
            DrawControlCol(new Rect(x, y, colW, 196f), "PLAYER A · HOST", new[]
            {
                ("Move", new[] { "W", "A", "S", "D" }), ("Look", new[] { "Mouse" }), ("Jump", new[] { "Space" }), ("Switch gravity", new[] { "E" })
            });
            DrawControlCol(new Rect(x + colW + 16f, y, colW, 196f), "PLAYER B · JOINS", new[]
            {
                ("Move", new[] { "W", "A", "S", "D" }), ("Look", new[] { "Mouse" }), ("Jump", new[] { "Space" }), ("Freeze · hold: steer", new[] { "E" })
            });
            MenuStyle.Text(new Rect(x, r.yMax - 84f, inner, 16f), "Solo: WASD or arrows move · E bends gravity · Q freezes.",
                UIKit.Face.Body, 12, MenuStyle.W50, 0f, TextAnchor.MiddleLeft);
            MenuStyle.Text(new Rect(x, r.yMax - 64f, inner, 16f), "Phones: joystick · drag to look · JUMP · POWER     C: camera · Hold R: checkpoint · Esc: pause",
                UIKit.Face.Body, 12, MenuStyle.W50, 0f, TextAnchor.MiddleLeft);
            if (MenuStyle.Button(new Rect(r.xMax - 26f - 96f, r.yMax - 26f - 34f, 96f, 34f), "Back", true)) CloseOverlay();
        }

        static void DrawControlCol(Rect r, string title, (string label, string[] keys)[] items)
        {
            MenuStyle.Box(r, MenuStyle.W25, 1f);
            MenuStyle.Text(new Rect(r.x + 14f, r.y + 14f, r.width - 28f, 16f), title, UIKit.Face.Display, 13, 1f, 0.2f, TextAnchor.UpperLeft);
            float y = r.y + 14f + 16f + 10f;
            for (int i = 0; i < items.Length; i++)
            {
                float rowH = 32f;
                MenuStyle.Text(new Rect(r.x + 14f, y, r.width * 0.55f, rowH), items[i].label, UIKit.Face.Body, 13, MenuStyle.W80, 0f, TextAnchor.MiddleLeft);
                float kx = r.xMax - 14f;
                for (int k = items[i].keys.Length - 1; k >= 0; k--)
                {
                    float kw = Mathf.Max(24f, MenuStyle.Measure(items[i].keys[k], UIKit.Face.Heavy, 11, 0f) + 14f);
                    kx -= kw;
                    MenuStyle.Key(kx, y + rowH * 0.5f, items[i].keys[k]);
                    kx -= 4f;
                }
                if (i < items.Length - 1) MenuStyle.DashedLine(r.x + 14f, r.xMax - 14f, y + rowH, MenuStyle.W25);
                y += rowH;
            }
        }

        void DrawQuit(float w, float h)
        {
            var r = Modal(w, h, Mathf.Min(w * 0.92f, 600f), 170f);
            float x = r.x + 26f, inner = r.width - 52f;
            MenuStyle.Text(new Rect(x, r.y + 26f, inner, 22f), "QUIT GAME?", UIKit.Face.Display, 18, 1f, 0.3f, TextAnchor.UpperLeft);
            MenuStyle.Text(new Rect(x, r.y + 26f + 22f + 16f, inner, 18f), "Unsaved progress will be lost.", UIKit.Face.Body, 14, MenuStyle.W50, 0f, TextAnchor.UpperLeft);
            var quit = new Rect(r.xMax - 26f - 96f, r.yMax - 26f - 34f, 96f, 34f);
            var stay = new Rect(quit.x - 10f - 96f, quit.y, 96f, 34f);
            if (MenuStyle.Button(stay, "Stay", true)) CloseOverlay();
            if (MenuStyle.Button(quit, "Quit")) Quit();
        }

        void DrawLaunch(float w, float h)
        {
            float cy = h * 0.5f - 50f;
            MenuStyle.Text(new Rect(0, cy, w, 12f), "LAUNCHING", UIKit.Face.Heavy, 10, MenuStyle.W50, 0.3f, TextAnchor.UpperCenter);
            MenuStyle.Text(new Rect(0, cy + 18f, w, 36f), Stages[stage].name.ToUpperInvariant(), UIKit.Face.Display, 28, 1f, 0.12f, TextAnchor.UpperCenter);
            string sub = (NetSession.Online ? (NetSession.IsHost ? "Room " + NetSession.RoomCode + " · Host" : "Room " + NetSession.RoomCode) : Modes[mode].name)
                         + (stage == 0 && cutscene ? " · Cutscene on" : "");
            MenuStyle.Text(new Rect(0, cy + 62f, w, 18f), sub, UIKit.Face.Body, 13, MenuStyle.W50, 0f, TextAnchor.UpperCenter);
            float bw = Mathf.Min(w * 0.7f, 360f);
            var bar = new Rect((w - bw) * 0.5f, cy + 62f + 18f + 18f, bw, 3f);
            UIKit.Fill(bar, MenuStyle.White(MenuStyle.W25));
            float t = Mathf.Clamp01((Time.unscaledTime - launchStart) / 1.8f);
            t = 1f - (1f - t) * (1f - t); // ease
            UIKit.Fill(new Rect(bar.x, bar.y, bar.width * t, bar.height), MenuStyle.White(1f));
        }

        // ---------- starting ----------

        /// <summary>Show "Launching …" with its loading bar, then start.</summary>
        void StartGame()
        {
            if (overlay == Overlay.Launch) return;
            if (NetSession.IsHost) NetSession.LockRoom(); // start with whoever is in
            overlay = Overlay.Launch;
            launchStart = Time.unscaledTime;
            joinEntry = false;
            Sfx.Play(SfxId.Checkpoint, 0.6f);
        }

        void StartGameNow()
        {
            if (stage == 0 && cutscene) GameFlow.StartNewGame();
            else GameFlow.LoadStage(stage + 1);
        }

        /// <summary>Testing two copies on one machine: -bghost [-bgstage N] [-bgnocut] starts a room and the game as soon
        /// as someone joins; -bgjoin &lt;code or IP&gt; joins one.</summary>
        void DevCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : "";
                switch (args[i])
                {
                    case "-bgstage": if (int.TryParse(next, out int st)) stage = Mathf.Clamp(st - 1, 0, Stages.Length - 1); break;
                    case "-bgnocut": cutscene = false; break;
                    case "-bghost": mode = HostMode; autoStart = true; break;
                    case "-bgjoin": mode = JoinMode; joinCode = next; break;
                }
            }
            if (autoStart && !Joining) { GameFlow.PlayerCount = GameFlow.MaxPlayers; NetSession.StartHost(); }
            if (Joining && joinCode.Length > 0) Connect();
        }

        // ---------- online room ----------

        bool RoomPanelOpen => overlay != Overlay.Launch && (joinEntry || NetSession.Online || NetSession.Busy);

        void LeaveRoom()
        {
            NetSession.Stop();
            joinEntry = false;
            Sfx.Play(SfxId.Plate, 0.5f);
        }

        void Connect()
        {
            if (NetSession.StartClient(joinCode)) joinEntry = false;
            Sfx.Play(SfxId.Plate, 0.6f);
        }

        void TypeKey(string k)
        {
            if (k == "<") { if (joinCode.Length > 0) joinCode = joinCode.Substring(0, joinCode.Length - 1); }
            else if (joinCode.Length < 15) joinCode += k;
            Sfx.Play(SfxId.Tick, 0.5f);
        }

        void RoomKeys()
        {
            if (PlayerKeys.Pressed(Key.Escape)) { LeaveRoom(); return; }
            if (joinEntry && !NetSession.Online)
            {
                Key[] digits = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
                Key[] pad = { Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9 };
                for (int i = 0; i < 10; i++) if (PlayerKeys.Pressed(digits[i]) || PlayerKeys.Pressed(pad[i])) TypeKey(i.ToString());
                for (Key k = Key.A; k <= Key.Z; k++) if (PlayerKeys.Pressed(k)) TypeKey(((char)('A' + (k - Key.A))).ToString());
                if (PlayerKeys.Pressed(Key.Period) || PlayerKeys.Pressed(Key.NumpadPeriod)) TypeKey(".");
                if (PlayerKeys.Pressed(Key.Backspace)) TypeKey("<");
                if (PlayerKeys.Pressed(Key.Enter) || PlayerKeys.Pressed(Key.NumpadEnter)) Connect();
                return;
            }
            if (NetSession.IsHost && NetSession.Connected && (Confirm() || (autoStart && NetSession.Full))) { autoStart = false; StartGame(); }
        }

        void DrawRoom(float w, float h)
        {
            if (joinEntry && !NetSession.Online)
            {
                var r = Modal(w, h, Mathf.Min(w * 0.92f, 520f), 560f);
                float x = r.x + 26f, inner = r.width - 52f, cx = r.center.x;
                MenuStyle.Text(new Rect(x, r.y + 26f, inner, 22f), "JOIN A ROOM", UIKit.Face.Display, 18, 1f, 0.3f, TextAnchor.UpperLeft);
                MenuStyle.Text(new Rect(x, r.y + 60f, inner, 18f), "Type the room code shown on your partner's screen (any network).", UIKit.Face.Body, 13, MenuStyle.W50, 0f, TextAnchor.UpperLeft);
                var field = new Rect(cx - 150f, r.y + 92f, 300f, 60f);
                MenuStyle.Box(field, MenuStyle.W50, 1f);
                MenuStyle.Brackets(field);
                MenuStyle.Text(field, joinCode.Length > 0 ? joinCode : "····", UIKit.Face.Display, 30, joinCode.Length > 0 ? 1f : MenuStyle.W25, 0.25f);
                const int cols = 10;
                float kw = (inner - (cols - 1) * 6f) / cols;
                for (int i = 0; i < Keypad.Length; i++)
                {
                    var k = new Rect(x + (i % cols) * (kw + 6f), r.y + 170f + (i / cols) * 52f, Keypad[i] == "<" ? kw * 2f + 6f : kw, 44f);
                    if (MenuStyle.Button(k, Keypad[i] == "<" ? "Del" : Keypad[i])) TypeKey(Keypad[i]);
                }
                MenuStyle.Paragraph(new Rect(x, r.y + 390f, inner, 60f),
                    (string.IsNullOrEmpty(NetSession.LastError) ? "" : NetSession.LastError + "\n") +
                    "Letters + digits = internet room (works on any network). 4 digits or an IP = same Wi-Fi room.", UIKit.Face.Body, 12, MenuStyle.W50, TextAnchor.UpperCenter);
                var connect = new Rect(r.xMax - 26f - 120f, r.yMax - 26f - 34f, 120f, 34f);
                if (MenuStyle.Button(new Rect(connect.x - 10f - 96f, connect.y, 96f, 34f), "Back")) { joinEntry = false; Sfx.Play(SfxId.Plate, 0.5f); }
                if (MenuStyle.Button(connect, "Connect", true)) Connect();
                return;
            }

            if (NetSession.Busy && !NetSession.Online)
            {
                var br = Modal(w, h, Mathf.Min(w * 0.92f, 520f), 170f);
                float bp = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
                MenuStyle.Text(new Rect(br.x, br.y + 60f, br.width, 24f), "CREATING ROOM…", UIKit.Face.Heavy, 18, 0.45f + 0.5f * bp, 0.15f, TextAnchor.UpperCenter);
                MenuStyle.Text(new Rect(br.x, br.y + 96f, br.width, 16f), "Connecting to Unity Relay", UIKit.Face.Body, 12, MenuStyle.W50, 0f, TextAnchor.UpperCenter);
                return;
            }

            if (NetSession.IsHost)
            {
                var r = Modal(w, h, Mathf.Min(w * 0.92f, 560f), 430f);
                float x = r.x + 26f, inner = r.width - 52f;
                MenuStyle.Text(new Rect(x, r.y + 26f, inner, 22f), "YOUR ROOM", UIKit.Face.Display, 18, 1f, 0.3f, TextAnchor.UpperLeft);
                MenuStyle.Text(new Rect(x, r.y + 70f, inner, 12f), "ROOM CODE", UIKit.Face.Heavy, 10, MenuStyle.W50, 0.3f, TextAnchor.UpperCenter);
                MenuStyle.Text(new Rect(x, r.y + 88f, inner, 80f), NetSession.RoomCode, UIKit.Face.Display, 72, 1f, 0.12f, TextAnchor.UpperCenter);
                string where = NetSession.ViaRelay ? "Internet room · works on any network"
                             : "Same Wi-Fi room" + (string.IsNullOrEmpty(NetSession.LocalAddress) ? "" : " · or IP " + NetSession.LocalAddress);
                MenuStyle.Text(new Rect(x, r.y + 184f, inner, 18f), where, UIKit.Face.Body, 13, MenuStyle.W50, 0f, TextAnchor.UpperCenter);
                MenuStyle.Line(x, r.y + 218f, inner, MenuStyle.W25);
                bool joined = NetSession.Connected;
                int seats = GameFlow.PlayerCount, inRoom = NetSession.Joined;
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
                MenuStyle.Text(new Rect(x, r.y + 232f, inner, 24f), NetSession.Full ? "EVERYONE'S IN" : "WAITING FOR PLAYERS…  " + inRoom + "/" + seats,
                    UIKit.Face.Heavy, 16, NetSession.Full ? 1f : 0.45f + 0.5f * pulse, 0.12f, TextAnchor.UpperCenter);
                DrawSeats(new Rect(x, r.y + 264f, inner, 40f), seats, inRoom);
                MenuStyle.Text(new Rect(x, r.y + 312f, inner, 16f), "You play A. On the other device: Mode → Join a Room, then type this code.", UIKit.Face.Body, 12, MenuStyle.W80, 0f, TextAnchor.UpperCenter);
                MenuStyle.Text(new Rect(x, r.y + 334f, inner, 16f), Stages[stage].name + (stage == 0 && cutscene ? "  ·  Cutscene on" : ""), UIKit.Face.Body, 12, MenuStyle.W50, 0f, TextAnchor.UpperCenter);
                var startBtn = new Rect(r.xMax - 26f - 120f, r.yMax - 26f - 34f, 120f, 34f);
                if (MenuStyle.Button(new Rect(startBtn.x - 10f - 130f, startBtn.y, 130f, 34f), "Close room")) LeaveRoom();
                if (joined) { if (MenuStyle.Button(startBtn, NetSession.Full ? "Start" : "Start · " + inRoom, true)) StartGame(); }
                else
                {
                    MenuStyle.Box(startBtn, MenuStyle.W25, 1f);
                    MenuStyle.Text(startBtn, "START", UIKit.Face.Heavy, 12, MenuStyle.W25, 0.22f);
                }
                return;
            }

            // joining device, waiting for the host
            var jr = Modal(w, h, Mathf.Min(w * 0.92f, 520f), 290f);
            float jx = jr.x + 26f, jin = jr.width - 52f;
            bool connected = NetSession.Connected;
            float p2 = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
            MenuStyle.Text(new Rect(jx, jr.y + 26f, jin, 22f), "ONLINE", UIKit.Face.Display, 18, 1f, 0.3f, TextAnchor.UpperLeft);
            MenuStyle.Text(new Rect(jx, jr.y + 80f, jin, 26f),
                connected ? "CONNECTED · ROOM " + NetSession.RoomCode : "LOOKING FOR ROOM " + (NetSession.RoomCode.Length > 0 ? NetSession.RoomCode : joinCode) + "…",
                UIKit.Face.Heavy, 18, connected ? 1f : 0.45f + 0.5f * p2, 0.08f, TextAnchor.UpperCenter);
            if (connected)
            {
                int me = NetSession.LocalPlayer;
                MenuStyle.Paragraph(new Rect(jx, jr.y + 116f, jin, 40f),
                    "You play " + GameFlow.Letter(me) + (GameFlow.HasGravity(me) ? " (Gravity Controller)" : " (Gravity Nullifier)") + ". The host starts the game.",
                    UIKit.Face.Body, 13, MenuStyle.W80);
                DrawSeats(new Rect(jx, jr.y + 160f, jin, 40f), GameFlow.PlayerCount, NetSession.Joined, me);
            }
            else
                MenuStyle.Paragraph(new Rect(jx, jr.y + 122f, jin, 50f), "Internet code: any network. 4-digit code: same Wi-Fi or hotspot as the host.",
                    UIKit.Face.Body, 13, MenuStyle.W80);
            if (MenuStyle.Button(new Rect(jr.xMax - 26f - 96f, jr.yMax - 26f - 34f, 96f, 34f), "Leave")) LeaveRoom();
        }

        /// <summary>One box per seat (A-D): filled when someone is in it, brackets on this device's own letter.</summary>
        static void DrawSeats(Rect r, int seats, int inRoom, int mine = 0)
        {
            const float size = 40f, gap = 12f;
            float x = r.center.x - (seats * size + (seats - 1) * gap) * 0.5f;
            for (int i = 0; i < seats; i++)
            {
                var box = new Rect(x + i * (size + gap), r.y, size, size);
                bool taken = i < inRoom;
                if (taken) UIKit.Fill(box, MenuStyle.White(MenuStyle.W12));
                MenuStyle.Box(box, taken ? 1f : MenuStyle.W25, 1f);
                if (i == mine) MenuStyle.Brackets(box);
                MenuStyle.Text(box, GameFlow.Letter(i), UIKit.Face.Display, 18, taken ? 1f : MenuStyle.W25, 0f);
            }
        }
    }
}
