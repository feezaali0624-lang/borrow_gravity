using UnityEditor;
using UnityEngine;

public static class Stage5Skeleton
{
    [MenuItem("BorrowGravity/Stage 5 - Add Placeholder Sections")]
    static void Add()
    {
        var root = GameObject.Find("Level_Stage5");
        if (root == null) { Debug.LogError("Level_Stage5 nahi mila (active hona chahiye)."); return; }
        var env = root.transform.Find("Environment");
        var game = root.transform.Find("Gameplay");
        if (env == null || game == null) { Debug.LogError("Level_Stage5 ke andar Environment ya Gameplay nahi hai."); return; }

        string[] names = { "5.1 Start Dock", "5.2 Gravity Gap", "5.3 Ring Sync", "5.4 Crumble Bridge", "5.5 Ship Repair" };
        float[] xs = { 0f, 30f, 72f, 115f, 152f };

        for (int i = 0; i < names.Length; i++)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.name = names[i];
            visual.transform.SetParent(env, false);
            visual.transform.localPosition = new Vector3(xs[i], 0f, 0f);
            visual.transform.localScale = new Vector3(14f, 1f, 6f);
            Undo.RegisterCreatedObjectUndo(visual, "Stage5 placeholder");

            var group = new GameObject(names[i]);
            group.transform.SetParent(game, false);
            group.transform.localPosition = new Vector3(xs[i], 0f, 0f);
            var floor = group.AddComponent<BoxCollider>();
            floor.size = new Vector3(14f, 1f, 6f);
            Undo.RegisterCreatedObjectUndo(group, "Stage5 group");
        }

        var goal = new GameObject("FinishGoal");
        goal.transform.SetParent(game, false);
        goal.transform.localPosition = new Vector3(xs[xs.Length - 1] + 4f, 2f, 0f);
        var bc = goal.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = new Vector3(4f, 4f, 6f);
        Undo.RegisterCreatedObjectUndo(goal, "Stage5 goal");

        Debug.Log("Stage 5 skeleton ban gaya. FinishGoal par GoalTrigger component lagana na bhoolna.");
    }
}