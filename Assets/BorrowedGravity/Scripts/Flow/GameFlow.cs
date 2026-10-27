using UnityEngine;
using UnityEngine.SceneManagement;

namespace BorrowGravity
{
    /// <summary>
    /// Level order and progress (unlocked stages are saved in PlayerPrefs). All levels live in one Game scene and are
    /// switched in place by <see cref="GameStages"/> — only the main menu is a separate scene. If the Game scene is
    /// missing from the build, the older one-scene-per-level layout is used instead.
    /// </summary>
    public static class GameFlow
    {
        public const string MenuScene = "MainMenu";
        public const string GameScene = "Game";
        public const string IntroScene = "Intro";
        public const string OutroScene = "Outro";
        public static readonly string[] StageScenes = { "Stage1", "Stage2", "Stage3", "Stage4", "Stage5" };
        const string UnlockKey = "bg_unlocked_stage";

        /// <summary>True when a stage was entered from the menu or a previous stage (shows the story intro).</summary>
        public static bool ShowIntro = true;

        /// <summary>The level the Game scene shows when it (re)loads: 0 intro, 1-4 stages, 5 ending.</summary>
        public static int PendingStage = 1;

        public const int MaxPlayers = 2;

        /// <summary>How many astronauts play (1-2): chosen by whoever starts the game / hosts the room.</summary>
        public static int PlayerCount = 2;

        public static bool Solo => PlayerCount <= 1;

        /// <summary>A bends gravity and B nullifies; a solo player has both powers.</summary>
        public static bool HasGravity(int playerIndex) => Solo || playerIndex % 2 == 0;
        public static bool HasNullify(int playerIndex) => Solo || playerIndex % 2 == 1;

        public static string Letter(int playerIndex) => ((char)('A' + Mathf.Clamp(playerIndex, 0, MaxPlayers - 1))).ToString();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ShowIntro = true;
            PendingStage = 1;
            PlayerCount = 2;
        }

        /// <summary>Phones default to 30 fps and dim the screen while you only touch it: run at 60 (30 on low-end phones,
        /// see <see cref="PerformanceGuard"/>) and stay awake.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void MobileSetup()
        {
            if (!Application.isMobilePlatform) return;
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            PerformanceGuard.Begin();
        }

        public static int Unlocked
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(UnlockKey, 1), 1, StageScenes.Length);
            set { PlayerPrefs.SetInt(UnlockKey, Mathf.Clamp(value, 1, StageScenes.Length)); PlayerPrefs.Save(); }
        }

        static bool SingleScene => Application.CanStreamedLevelBeLoaded(GameScene);

        public static void LoadStage(int stage)
        {
            ShowIntro = true;
            GoTo(Mathf.Clamp(stage, 1, StageScenes.Length));
        }

        /// <summary>New game: the opening cutscene, which then goes on to Stage 1.</summary>
        public static void StartNewGame() => GoTo(GameStages.Intro);

        /// <summary>Called when the opening cutscene ends: straight into Stage 1 (the cutscene already told the story).</summary>
        public static void AfterIntro()
        {
            ShowIntro = false;
            GoTo(1);
        }

        /// <summary>Last stage done: the ending cutscene (engine fixed, ship takes off), then the menu.</summary>
        public static void FinishGame() => GoTo(GameStages.Outro);

        public static void AfterOutro() => LoadMenu();

        /// <summary>Restart the current stage from scratch (reloads the scene so every puzzle is fresh).</summary>
        public static void ReloadStage()
        {
            ShowIntro = false;
            if (GameStages.Instance != null) PendingStage = GameStages.Instance.Current;
            Prepare();
            Load(SceneManager.GetActiveScene().name);
        }

        public static void LoadMenu()
        {
            Prepare();
            Load(MenuScene);
        }

        /// <summary>Show a level: switched in place when the Game scene is already running, otherwise loads it.</summary>
        static void GoTo(int level)
        {
            if (NetSession.IsClient) return; // the host decides; its choice arrives over the network
            Prepare();
            PendingStage = level;
            if (!SingleScene) { LoadSeparateScene(level); return; }
            var stages = GameStages.Instance;
            if (stages != null && SceneManager.GetActiveScene().name == GameScene && stages.Has(level) && !stages.WasShown(level)) stages.Show(level);
            else Load(GameScene);
        }

        static void Prepare()
        {
            Time.timeScale = 1f;
            Sfx.StopAllLoops();
        }

        static void LoadSeparateScene(int level)
        {
            if (level == GameStages.Intro) Load(Application.CanStreamedLevelBeLoaded(IntroScene) ? IntroScene : StageScenes[0]);
            else if (level == GameStages.Outro) Load(Application.CanStreamedLevelBeLoaded(OutroScene) ? OutroScene : MenuScene);
            else Load(StageScenes[level - 1]);
        }

        /// <summary>
        /// Every scene change goes through here. Online the host decides and tells the other phone; the joining phone
        /// ignores its own requests (e.g. its copy of the cutscene ending) and waits for the host's.
        /// </summary>
        static void Load(string scene)
        {
            if (NetSession.IsClient) return;
            if (NetSession.IsHost) NetSession.SendScene(scene, ShowIntro);
            SceneManager.LoadScene(scene);
        }

        public static void StageCompleted(int stage)
        {
            if (stage + 1 > Unlocked) Unlocked = stage + 1;
        }

        public static bool HasNext(int stage) => stage < StageScenes.Length;
    }
}
