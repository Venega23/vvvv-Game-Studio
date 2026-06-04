using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DisallowMultipleComponent]
public class RumiInteractionAnimationController : MonoBehaviour
{
    public Animator animator;

    private PlayableGraph graph;
    private AnimationClipPlayable clipPlayable;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        var model = GameObject.Find("PlayerModel");
        if (model != null && model.GetComponent<RumiInteractionAnimationController>() == null)
        {
            model.AddComponent<RumiInteractionAnimationController>();
        }
    }

    private void Awake()
    {
        AutoWire();
    }

    private void OnDisable()
    {
        StopInteractionAnimation();
    }

    private void OnDestroy()
    {
        StopInteractionAnimation();
    }

    public void PlayLoop(AnimationClip clip)
    {
        if (clip == null)
        {
            return;
        }

        AutoWire();
        if (animator == null)
        {
            return;
        }

        StopInteractionAnimation();
        graph = PlayableGraph.Create("Rumi Interaction Animation");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        clipPlayable = AnimationClipPlayable.Create(graph, clip);
        clipPlayable.SetApplyFootIK(true);
        clipPlayable.SetApplyPlayableIK(false);

        var output = AnimationPlayableOutput.Create(graph, "InteractionAnimation", animator);
        output.SetSourcePlayable(clipPlayable);
        graph.Play();
    }

    public void StopInteractionAnimation()
    {
        if (graph.IsValid())
        {
            graph.Destroy();
        }
    }

    private void AutoWire()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
        }
    }
}
