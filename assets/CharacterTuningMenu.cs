using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class CharacterTuningMenu : MonoBehaviour
{
    private const string PrefPrefix = "StudioCharacterTuning.";
    private const int WindowId = 837480;

    public static bool IsOpen { get; private set; }

    [Header("Input")]
    public Key toggleKey = Key.F4;

    private Rect windowRect = new Rect(48f, 84f, 430f, 610f);
    private PlayerMove tuningTarget;
    private Vector2 scroll;
    private GUIStyle titleStyle;
    private GUIStyle labelStyle;
    private GUIStyle mutedStyle;
    private GUIStyle panelStyle;
    private GUIStyle buttonStyle;
    private GUIStyle activeButtonStyle;
    private double nextAutoSaveTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (FindAnyObjectByType<CharacterTuningMenu>() != null)
        {
            return;
        }

        var go = new GameObject("CharacterTuningMenu");
        DontDestroyOnLoad(go);
        go.AddComponent<CharacterTuningMenu>();
    }

    private void Start()
    {
        StartCoroutine(ApplySavedSettingsAfterSceneBoot());
    }

    private IEnumerator ApplySavedSettingsAfterSceneBoot()
    {
        yield return null;
        yield return null;
        ApplySavedSettingsToAll();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard[toggleKey].wasPressedThisFrame)
        {
            SetOpen(!IsOpen);
            return;
        }

        if (IsOpen && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetOpen(false);
        }
    }

    private void OnDisable()
    {
        SetOpen(false);
    }

    private void OnGUI()
    {
        if (!IsOpen)
        {
            return;
        }

        EnsureStyles();
        ClampWindowToScreen();
        windowRect = GUI.Window(WindowId, windowRect, DrawWindow, "Character Settings");
    }

    private void DrawWindow(int id)
    {
        EnsureTarget();

        GUILayout.BeginVertical();
        GUILayout.Label("Character tuning", titleStyle);
        GUILayout.Label("F4 toggles this menu. Values apply instantly and are saved per character.", mutedStyle);
        GUILayout.Space(8f);

        DrawCharacterPicker();
        GUILayout.Space(8f);

        scroll = GUILayout.BeginScrollView(scroll, false, true);
        if (tuningTarget == null)
        {
            GUILayout.Label("No PlayerMove character found.", labelStyle);
        }
        else
        {
            DrawBodyPanel(tuningTarget);
            GUILayout.Space(8f);
            DrawMovementPanel(tuningTarget);
            GUILayout.Space(8f);
            DrawAbilityPanel(tuningTarget);
            GUILayout.Space(8f);
            DrawUtilityButtons(tuningTarget);
        }
        GUILayout.EndScrollView();

        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.Label(tuningTarget != null ? "Editing: " + CleanName(tuningTarget.name) : "Editing: none", mutedStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", activeButtonStyle, GUILayout.Width(96f), GUILayout.Height(30f)))
        {
            SetOpen(false);
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 32f));
    }

    private void DrawCharacterPicker()
    {
        var characters = GetCharacters();
        GUILayout.BeginVertical(panelStyle);
        GUILayout.Label("Target", labelStyle);
        GUILayout.BeginHorizontal();
        foreach (var character in characters)
        {
            if (character == null)
            {
                continue;
            }

            var style = character == tuningTarget ? activeButtonStyle : buttonStyle;
            if (GUILayout.Button(CleanName(character.name), style, GUILayout.Height(30f)))
            {
                tuningTarget = character;
            }
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    private void DrawBodyPanel(PlayerMove move)
    {
        GUILayout.BeginVertical(panelStyle);
        GUILayout.Label("Body", labelStyle);
        var scale = DrawSlider("Character size", GetUniformScale(move), 0.2f, 4f, "x");
        SetUniformScale(move, scale);
        GUILayout.EndVertical();
    }

    private void DrawMovementPanel(PlayerMove move)
    {
        GUILayout.BeginVertical(panelStyle);
        GUILayout.Label("Movement", labelStyle);
        move.speed = DrawSlider("Walk speed", move.speed, 0.5f, 18f, "m/s");
        move.runSpeed = DrawSlider("Run speed", move.runSpeed, 0.5f, 28f, "m/s");
        move.crouchSpeed = DrawSlider("Crouch speed", move.crouchSpeed, 0.2f, 10f, "m/s");
        move.turnSpeed = DrawSlider("Turn speed", move.turnSpeed, 1f, 30f, "");
        move.flyVerticalSpeed = DrawSlider("Fly vertical speed", move.flyVerticalSpeed, 1f, 20f, "m/s");
        GUILayout.EndVertical();
    }

    private void DrawAbilityPanel(PlayerMove move)
    {
        GUILayout.BeginVertical(panelStyle);
        GUILayout.Label("Jump and crouch", labelStyle);
        move.jumpForce = DrawSlider("Jump force", move.jumpForce, 0f, 18f, "");
        var gravityPull = DrawSlider("Gravity pull", Mathf.Abs(move.gravity), 5f, 60f, "");
        move.gravity = -gravityPull;
        move.groundedStickForce = -DrawSlider("Ground stick", Mathf.Abs(move.groundedStickForce), 0.5f, 8f, "");
        move.crouchHeight = DrawSlider("Crouch height", move.crouchHeight, 0.5f, 2.4f, "m");
        move.crouchBlendSpeed = DrawSlider("Crouch blend", move.crouchBlendSpeed, 1f, 30f, "");
        move.canJump = GUILayout.Toggle(move.canJump, "Allow jump");
        move.canCrouch = GUILayout.Toggle(move.canCrouch, "Allow crouch");
        move.ignoreWorldCollision = GUILayout.Toggle(move.ignoreWorldCollision, "Ignore world collision");
        GUILayout.EndVertical();
    }

    private void DrawUtilityButtons(PlayerMove move)
    {
        GUILayout.BeginVertical(panelStyle);
        GUILayout.Label("Presets", labelStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save", buttonStyle, GUILayout.Height(30f)))
        {
            SaveSettings(move);
        }

        if (GUILayout.Button("Apply to all", buttonStyle, GUILayout.Height(30f)))
        {
            ApplyCurrentToAll(move);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Default Rumi/Zoey", buttonStyle, GUILayout.Height(30f)))
        {
            ApplyHumanoidDefaults(move);
            SaveSettings(move);
        }

        if (GUILayout.Button("Default Mira", buttonStyle, GUILayout.Height(30f)))
        {
            ApplyMiraDefaults(move);
            SaveSettings(move);
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        if (GUI.changed && Time.unscaledTimeAsDouble >= nextAutoSaveTime)
        {
            SaveSettings(move);
            nextAutoSaveTime = Time.unscaledTimeAsDouble + 0.25d;
        }
    }

    private float DrawSlider(string label, float value, float min, float max, string suffix)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(138f));
        var newValue = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(170f));
        newValue = Mathf.Round(newValue * 100f) / 100f;
        GUILayout.Label(newValue.ToString("0.##") + (string.IsNullOrEmpty(suffix) ? string.Empty : " " + suffix), mutedStyle, GUILayout.Width(70f));
        GUILayout.EndHorizontal();
        return newValue;
    }

    private void EnsureTarget()
    {
        if (tuningTarget != null)
        {
            return;
        }

        tuningTarget = FindActiveCharacter();
        if (tuningTarget == null)
        {
            var characters = GetCharacters();
            if (characters.Count > 0)
            {
                tuningTarget = characters[0];
            }
        }
    }

    private PlayerMove FindActiveCharacter()
    {
        var characters = FindObjectsByType<PlayerMove>(FindObjectsInactive.Include);
        foreach (var character in characters)
        {
            if (character != null && character.enabled)
            {
                return character;
            }
        }

        return null;
    }

    private List<PlayerMove> GetCharacters()
    {
        var result = new List<PlayerMove>();
        var all = FindObjectsByType<PlayerMove>(FindObjectsInactive.Include);
        foreach (var move in all)
        {
            if (move == null || result.Contains(move))
            {
                continue;
            }

            result.Add(move);
        }

        result.Sort((a, b) => string.Compare(CleanName(a.name), CleanName(b.name), System.StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private void ApplyCurrentToAll(PlayerMove source)
    {
        var characters = GetCharacters();
        foreach (var target in characters)
        {
            if (target == null)
            {
                continue;
            }

            CopySettings(source, target);
            SaveSettings(target);
        }
    }

    private void ApplySavedSettingsToAll()
    {
        var characters = GetCharacters();
        foreach (var target in characters)
        {
            LoadSettings(target);
        }
    }

    private static void CopySettings(PlayerMove source, PlayerMove target)
    {
        target.speed = source.speed;
        target.runSpeed = source.runSpeed;
        target.crouchSpeed = source.crouchSpeed;
        target.turnSpeed = source.turnSpeed;
        target.flyVerticalSpeed = source.flyVerticalSpeed;
        target.jumpForce = source.jumpForce;
        target.gravity = source.gravity;
        target.groundedStickForce = source.groundedStickForce;
        target.crouchHeight = source.crouchHeight;
        target.crouchBlendSpeed = source.crouchBlendSpeed;
        target.canJump = source.canJump;
        target.canCrouch = source.canCrouch;
        target.ignoreWorldCollision = source.ignoreWorldCollision;
        SetUniformScale(target, GetUniformScale(source));
    }

    private static void ApplyHumanoidDefaults(PlayerMove move)
    {
        move.speed = 5f;
        move.runSpeed = 8f;
        move.crouchSpeed = 2.5f;
        move.turnSpeed = 12f;
        move.flyVerticalSpeed = 5f;
        move.jumpForce = 7f;
        move.gravity = -24f;
        move.groundedStickForce = -2f;
        move.crouchHeight = 1.15f;
        move.crouchBlendSpeed = 12f;
        move.canJump = true;
        move.canCrouch = true;
        move.ignoreWorldCollision = false;
        SetUniformScale(move, 1f);
    }

    private static void ApplyMiraDefaults(PlayerMove move)
    {
        move.speed = 4f;
        move.runSpeed = 6f;
        move.crouchSpeed = 2f;
        move.turnSpeed = 10f;
        move.flyVerticalSpeed = 4f;
        move.jumpForce = 6f;
        move.gravity = -24f;
        move.groundedStickForce = -2f;
        move.crouchHeight = 1.15f;
        move.crouchBlendSpeed = 12f;
        move.canJump = false;
        move.canCrouch = false;
        move.ignoreWorldCollision = false;
    }

    private static void SaveSettings(PlayerMove move)
    {
        if (move == null)
        {
            return;
        }

        var key = GetPrefsKey(move);
        PlayerPrefs.SetFloat(key + ".speed", move.speed);
        PlayerPrefs.SetFloat(key + ".runSpeed", move.runSpeed);
        PlayerPrefs.SetFloat(key + ".crouchSpeed", move.crouchSpeed);
        PlayerPrefs.SetFloat(key + ".turnSpeed", move.turnSpeed);
        PlayerPrefs.SetFloat(key + ".flyVerticalSpeed", move.flyVerticalSpeed);
        PlayerPrefs.SetFloat(key + ".jumpForce", move.jumpForce);
        PlayerPrefs.SetFloat(key + ".gravity", move.gravity);
        PlayerPrefs.SetFloat(key + ".groundedStickForce", move.groundedStickForce);
        PlayerPrefs.SetFloat(key + ".crouchHeight", move.crouchHeight);
        PlayerPrefs.SetFloat(key + ".crouchBlendSpeed", move.crouchBlendSpeed);
        PlayerPrefs.SetFloat(key + ".characterScale", GetUniformScale(move));
        PlayerPrefs.SetInt(key + ".canJump", move.canJump ? 1 : 0);
        PlayerPrefs.SetInt(key + ".canCrouch", move.canCrouch ? 1 : 0);
        PlayerPrefs.SetInt(key + ".ignoreWorldCollision", move.ignoreWorldCollision ? 1 : 0);
        PlayerPrefs.SetInt(key + ".saved", 1);
        PlayerPrefs.Save();
    }

    private static void LoadSettings(PlayerMove move)
    {
        if (move == null)
        {
            return;
        }

        var key = GetPrefsKey(move);
        if (PlayerPrefs.GetInt(key + ".saved", 0) == 0)
        {
            return;
        }

        move.speed = PlayerPrefs.GetFloat(key + ".speed", move.speed);
        move.runSpeed = PlayerPrefs.GetFloat(key + ".runSpeed", move.runSpeed);
        move.crouchSpeed = PlayerPrefs.GetFloat(key + ".crouchSpeed", move.crouchSpeed);
        move.turnSpeed = PlayerPrefs.GetFloat(key + ".turnSpeed", move.turnSpeed);
        move.flyVerticalSpeed = PlayerPrefs.GetFloat(key + ".flyVerticalSpeed", move.flyVerticalSpeed);
        move.jumpForce = PlayerPrefs.GetFloat(key + ".jumpForce", move.jumpForce);
        move.gravity = PlayerPrefs.GetFloat(key + ".gravity", move.gravity);
        move.groundedStickForce = PlayerPrefs.GetFloat(key + ".groundedStickForce", move.groundedStickForce);
        move.crouchHeight = PlayerPrefs.GetFloat(key + ".crouchHeight", move.crouchHeight);
        move.crouchBlendSpeed = PlayerPrefs.GetFloat(key + ".crouchBlendSpeed", move.crouchBlendSpeed);
        if (PlayerPrefs.HasKey(key + ".characterScale"))
        {
            SetUniformScale(move, PlayerPrefs.GetFloat(key + ".characterScale", GetUniformScale(move)));
        }
        move.canJump = PlayerPrefs.GetInt(key + ".canJump", move.canJump ? 1 : 0) == 1;
        move.canCrouch = PlayerPrefs.GetInt(key + ".canCrouch", move.canCrouch ? 1 : 0) == 1;
        move.ignoreWorldCollision = PlayerPrefs.GetInt(key + ".ignoreWorldCollision", move.ignoreWorldCollision ? 1 : 0) == 1;
    }

    private static float GetUniformScale(PlayerMove move)
    {
        if (move == null)
        {
            return 1f;
        }

        var scale = move.transform.localScale;
        return Mathf.Clamp((Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f, 0.2f, 4f);
    }

    private static void SetUniformScale(PlayerMove move, float scale)
    {
        if (move == null)
        {
            return;
        }

        scale = Mathf.Clamp(scale, 0.2f, 4f);
        move.transform.localScale = Vector3.one * scale;
    }

    private static string GetPrefsKey(PlayerMove move)
    {
        return PrefPrefix + CleanName(move.name);
    }

    private void SetOpen(bool open)
    {
        IsOpen = open;
        if (open)
        {
            EnsureTarget();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (!SimpleInventoryWindow.IsOpen && !MapSwitcher.IsMenuOpen && !CharacterActionRecorder.IsMenuOpen && !SurfaceItemPlacer.BlocksToolInput)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void ClampWindowToScreen()
    {
        windowRect.width = Mathf.Min(windowRect.width, Mathf.Max(360f, Screen.width - 16f));
        windowRect.height = Mathf.Min(windowRect.height, Mathf.Max(420f, Screen.height - 16f));
        windowRect.x = Mathf.Clamp(windowRect.x, 4f, Mathf.Max(4f, Screen.width - windowRect.width - 4f));
        windowRect.y = Mathf.Clamp(windowRect.y, 4f, Mathf.Max(4f, Screen.height - windowRect.height - 4f));
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
        {
            return;
        }

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.88f, 0.93f, 0.95f) }
        };

        mutedStyle = new GUIStyle(GUI.skin.label)
        {
            normal = { textColor = new Color(0.62f, 0.70f, 0.74f) },
            wordWrap = true
        };

        panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(10, 10, 8, 10)
        };

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontStyle = FontStyle.Bold
        };

        activeButtonStyle = new GUIStyle(buttonStyle);
        activeButtonStyle.normal.textColor = Color.white;
    }

    private static string CleanName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Character";
        }

        return value.Replace("(Clone)", string.Empty).Trim();
    }
}
