using UnityEngine;

namespace BorrowGravity
{
    /// <summary>
    /// References to the HUD icon textures (Assets/Art/BorrowedGravity/UI). Field names match the PNG names;
    /// "Borrowed Gravity → Art Kit → 1. Build Materials + Prefabs" fills this asset automatically.
    /// </summary>
    [CreateAssetMenu(menuName = "Borrowed Gravity/HUD Icons", fileName = "HudIcons")]
    public class HudIcons : ScriptableObject
    {
        [Header("Powers")]
        public Texture2D Power_A_GravityController;
        public Texture2D Power_B_GravityNullifier;
        public Texture2D Ring_Cooldown, Ring_Track, Ring_Active;

        [Header("Gravity")]
        public Texture2D Gravity_Down, Gravity_Up, Gravity_Left, Gravity_Right, Gravity_Off, Gravity_Zero;

        [Header("Hazards")]
        public Texture2D Warn_General, Warn_FallingDebris, Warn_SwingingCable, Warn_BridgeCrack;
        public Texture2D Warn_GravityShift, Warn_Decompression, Warn_Electric;
        public Texture2D Countdown_Ring, Danger_Pulse;

        [Header("Engine")]
        public Texture2D Icon_Engine, Meter_Engine_Frame, Meter_Fill, Tilt_Gauge, Tilt_Needle;

        [Header("Checkpoint / objective")]
        public Texture2D Checkpoint_Badge, Banner_Checkpoint, Banner_Objective, Banner_Danger;
        public Texture2D Objective_Station, Objective_Engine, Objective_Ship;

        [Header("Players")]
        public Texture2D Tag_Player_A, Tag_Player_B, Offscreen_Arrow;

        public Texture2D Warning(HudHazard h)
        {
            switch (h)
            {
                case HudHazard.FallingDebris: return Warn_FallingDebris;
                case HudHazard.SwingingCable: return Warn_SwingingCable;
                case HudHazard.BridgeCrack: return Warn_BridgeCrack;
                case HudHazard.GravityShift: return Warn_GravityShift;
                case HudHazard.Decompression: return Warn_Decompression;
                case HudHazard.Electric: return Warn_Electric;
                default: return Warn_General;
            }
        }

        public Texture2D Objective(HudObjective o) =>
            o == HudObjective.FindEngine ? Objective_Engine : o == HudObjective.ReturnToShip ? Objective_Ship : Objective_Station;
    }

    public enum HudHazard { General, FallingDebris, SwingingCable, BridgeCrack, GravityShift, Decompression, Electric }

    public enum HudObjective { ReachStation, FindEngine, ReturnToShip, CrossEventHorizon, RepairShip }
}
