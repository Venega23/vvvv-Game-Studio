using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public class CharacterSwitcher : MonoBehaviour
{
    public Key switchKey = Key.Tab;
    public CameraFollow cameraFollow;
    public PlayerMove rumi;
    public PlayerMove zoey;
    public PlayerMove mario;
    public PlayerMove luigi;
    public PlayerMove mira;
    public int activeIndex;

    private readonly List<PlayerMove> characters = new();
    public PlayerMove ActiveCharacter => characters.Count > 0 && activeIndex >= 0 && activeIndex < characters.Count ? characters[activeIndex] : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<CharacterSwitcher>() != null)
        {
            return;
        }

        var go = new GameObject("CharacterSwitcher");
        DontDestroyOnLoad(go);
        go.AddComponent<CharacterSwitcher>();
    }

    private void Start()
    {
        DiscoverCharacters();
        SetActiveCharacter(0, true);
    }

    private void Update()
    {
        if (Keyboard.current == null || characters.Count < 2 || CharacterActionRecorder.IsRenderingVideo)
        {
            return;
        }

        if (Keyboard.current[switchKey].wasPressedThisFrame)
        {
            SetActiveCharacter((activeIndex + 1) % characters.Count, false);
        }
    }

    private void DiscoverCharacters()
    {
        characters.Clear();

        cameraFollow = cameraFollow != null ? cameraFollow : Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        rumi = rumi != null ? rumi : FindMove("Capsule", addIfMissing: false);
        zoey = zoey != null ? zoey : FindMove("Zoey", addIfMissing: true);
        mario = mario != null ? mario : FindMove("Mario", addIfMissing: true);
        luigi = luigi != null ? luigi : FindMove("Luigi", addIfMissing: true);
        mira = mira != null ? mira : FindMove("MiraHead_NPC", addIfMissing: true);

        ConfigureHumanoid(rumi);
        ConfigureHumanoid(zoey);
        ConfigureHumanoid(mario);
        ConfigureHumanoid(luigi);
        ConfigureMira(mira);

        AddCharacter(rumi);
        AddCharacter(zoey);
        AddCharacter(mario);
        AddCharacter(luigi);
        AddCharacter(mira);

        if (cameraFollow != null)
        {
            cameraFollow.SetSelectableTargets(characters.Select(character => character.transform));
        }
    }

    private PlayerMove FindMove(string objectName, bool addIfMissing)
    {
        var go = GameObject.Find(objectName);
        if (go == null)
        {
            return null;
        }

        var move = go.GetComponent<PlayerMove>();
        if (move == null && addIfMissing)
        {
            move = go.AddComponent<PlayerMove>();
        }

        return move;
    }

    private static void ConfigureHumanoid(PlayerMove move)
    {
        if (move == null)
        {
            return;
        }

        move.speed = Mathf.Max(move.speed, 5f);
        move.runSpeed = Mathf.Max(move.runSpeed, 8f);
        move.crouchSpeed = move.crouchSpeed <= 0f ? 2.5f : move.crouchSpeed;
        move.jumpForce = Mathf.Max(move.jumpForce, 7f);
        move.turnSpeed = Mathf.Max(move.turnSpeed, 12f);
        move.useCharacterController = true;
        move.preserveVerticalPosition = false;
        move.canJump = true;
        move.canCrouch = true;
        move.ignoreWorldCollision = false;
        move.groundY = 0f;
    }

    private static void ConfigureMira(PlayerMove move)
    {
        if (move == null)
        {
            return;
        }

        move.speed = 4f;
        move.runSpeed = 6f;
        move.crouchSpeed = 2f;
        move.jumpForce = 6f;
        move.turnSpeed = 10f;
        move.useCharacterController = true;
        move.preserveVerticalPosition = true;
        move.canJump = false;
        move.canCrouch = false;
        move.ignoreWorldCollision = false;
    }

    private void AddCharacter(PlayerMove move)
    {
        if (move != null && !characters.Contains(move))
        {
            characters.Add(move);
        }
    }

    private void SetActiveCharacter(int index, bool immediate)
    {
        if (characters.Count == 0)
        {
            return;
        }

        activeIndex = Mathf.Clamp(index, 0, characters.Count - 1);
        for (var i = 0; i < characters.Count; i++)
        {
            var move = characters[i];
            var active = i == activeIndex;

            if (!active)
            {
                move.ClearInputState();
            }

            move.enabled = active;
            var cc = move.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = active;
            }
            
            if (active && Camera.main != null)
            {
                move.cameraTransform = Camera.main.transform;
            }
        }

        var activeMove = characters[activeIndex];
        if (cameraFollow != null)
        {
            cameraFollow.developerCamera = false;
            cameraFollow.followControlledCharacter = true;
            cameraFollow.SetControlledTarget(activeMove.transform, immediate);
        }
    }
}
