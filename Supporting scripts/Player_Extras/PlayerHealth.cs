using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public class PlayerHealthLayerTag
{
    public int layer;
    public string tag = "Untagged";
}

public class PlayerHealth : MonoBehaviour
{
    [Header("Script State")]
    [Tooltip("If false, this script forces itself to stay enabled. Set to true to allow disabling.")]
    public bool Free = false;

    [Header("Health")]
    [Tooltip("Maximum health of the player")]
    public float maxHealth = 200f;

    [Header("Mana Healing")]
    [Tooltip("Heal rate (HP/sec) at the moment R is pressed")]
    public float initialHealRate = 25f;
    [Tooltip("Heal rate multiplier per second of holding R (2 = doubles every second). Resets when R is released")]
    public float healRateGrowthPerSecond = 2f;
    [Tooltip("Health restored per Mez spent (1 Mez = 5 HP). Mana cost = health healed / this")]
    public float healthPerMez = 5f;

    [Header("References")]
    [Tooltip("Player's mana system - used to draw mana for healing")]
    public AdvanceManaSystem manaSystem;

    [Tooltip("If on, health will never drop below half of max health, no matter the damage")]
    public bool invincibility = false;

    [Header("Health Bar UI")]
    [Tooltip("Front bar - Image set to Type: Filled, Method: Horizontal, Origin: Left. Snaps to current health instantly.")]
    public Image frontHealthBar;
    [Tooltip("Delayed bar - identical Image setup underneath the front bar. Waits, then drains to catch up (Elden Ring style).")]
    public Image delayedHealthBar;
    [Tooltip("Seconds to wait after taking damage before the delayed bar starts draining")]
    public float delayedBarDelay = 0.2f;
    [Tooltip("Speed (HP/sec) the delayed bar drains at once it starts")]
    public float delayedBarDrainSpeed = 30f;

    [Header("Death Sequence (No Anchor)")]
    [Tooltip("k in timeScale = e^(-k * t), t = real seconds since death. 0 = no slow-down.")]
    public float timeSlowRate = 3f;
    [Tooltip("REAL seconds (unscaled) after death before the Final Death Objects are enabled.")]
    public float finalObjectsDelay = 0.5f;
    [Tooltip("Objects that get enabled once the delay above has passed after death.")]
    public List<GameObject> finalDeathObjects = new List<GameObject>();

    [Header("Death Materials")]
    [Tooltip("Applied to every renderer in the scene on death (including particle systems and particle trails), except the exceptions below.")]
    public Material sceneMaterial;
    [Tooltip("Applied to every renderer whose object matches one of the layer + tag pairs below.")]
    public Material exceptionMaterial;
    [Tooltip("Skybox applied on death. Leave empty to keep the current skybox.")]
    public Material skyboxMaterial;
    [Tooltip("Objects matching BOTH the layer and the tag of an element get the Exception Material instead of the Scene Material.")]
    public List<PlayerHealthLayerTag> exceptions = new List<PlayerHealthLayerTag>();

    [Header("Debug")]
    public bool showDebugLogs = false;

    public AnchorSystem anchorSystem;

    [SerializeField] private float currentHealth;

    private float delayedDisplayHealth;
    private float delayedBarTimer;
    private float previousHealth;
    private float healHoldTime;
    private bool isDying;
    private bool timeSlowed;

    public float CurrentHealth => currentHealth;
    public float HealthPercentage => currentHealth / maxHealth;

    private void OnValidate()
    {
        timeSlowRate = Mathf.Max(0f, timeSlowRate);
        finalObjectsDelay = Mathf.Max(0f, finalObjectsDelay);
    }

    private void Start()
    {
        currentHealth = maxHealth;
        previousHealth = currentHealth;
        delayedDisplayHealth = currentHealth;
        UpdateHealthBars();
    }

    private void Update()
    {
        if (!isDying && Input.GetKey(KeyCode.R))
            TryHeal();
        else
            healHoldTime = 0f;

        UpdateHealthBars();
    }

    private void UpdateHealthBars()
    {
        if (currentHealth < previousHealth)
        {
            delayedBarTimer = delayedBarDelay;
        }
        previousHealth = currentHealth;

        if (frontHealthBar != null)
            frontHealthBar.fillAmount = HealthPercentage;

        if (delayedDisplayHealth < currentHealth)
        {
            delayedDisplayHealth = currentHealth;
        }
        else if (delayedDisplayHealth > currentHealth)
        {
            if (delayedBarTimer > 0f)
            {
                delayedBarTimer -= Time.unscaledDeltaTime;
            }
            else
            {
                delayedDisplayHealth = Mathf.Max(currentHealth, delayedDisplayHealth - delayedBarDrainSpeed * Time.unscaledDeltaTime);
            }
        }

        if (delayedHealthBar != null)
            delayedHealthBar.fillAmount = delayedDisplayHealth / maxHealth;
    }

    private void TryHeal()
    {
        if (currentHealth >= maxHealth || manaSystem == null)
        {
            healHoldTime = 0f;
            return;
        }

        float rate = initialHealRate * Mathf.Pow(healRateGrowthPerSecond, healHoldTime);
        float healAmount = Mathf.Min(rate * Time.deltaTime, maxHealth - currentHealth);
        float manaCost = healAmount / healthPerMez;

        if (manaSystem.UseMana(manaCost))
        {
            currentHealth += healAmount;
            healHoldTime += Time.deltaTime;

            if (showDebugLogs)
                Debug.Log($"[PlayerHealth] Healed {healAmount:F2} HP at {rate:F1} HP/s. HP: {currentHealth:F2}/{maxHealth}");
        }
    }

    public void TakeDamage(float damage)
    {
        if (isDying) return;
        if (currentHealth <= 0f) return;

        float floor = 0f;
        if (invincibility)
            floor = Mathf.Min(currentHealth, maxHealth * 0.5f);

        currentHealth = Mathf.Max(floor, currentHealth - damage);

        if (showDebugLogs)
            Debug.Log($"[PlayerHealth] Took {damage} damage. HP: {currentHealth:F2}/{maxHealth}");

        if (currentHealth <= 0f)
            OnDeath();
    }

    private void OnDeath()
    {
        if (anchorSystem != null)
        {
            anchorSystem.OnPlayerDeath();
            if (currentHealth > 0f) return;
        }

        if (!isDying)
            StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
        isDying = true;
        timeSlowed = true;

        ApplyDeathMaterials();

        if (skyboxMaterial != null)
        {
            RenderSettings.skybox = skyboxMaterial;
            DynamicGI.UpdateEnvironment();
        }

        bool finalObjectsEnabled = false;
        float t = 0f;

        while (true)
        {
            t += Time.unscaledDeltaTime;
            Time.timeScale = Mathf.Exp(-timeSlowRate * t);

            if (!finalObjectsEnabled && t >= finalObjectsDelay)
            {
                EnableFinalDeathObjects();
                finalObjectsEnabled = true;
            }

            yield return null;
        }
    }

    private void EnableFinalDeathObjects()
    {
        foreach (GameObject go in finalDeathObjects)
        {
            if (go != null)
                go.SetActive(true);
        }
    }

    private void ApplyDeathMaterials()
    {

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;

            Material targetMaterial = IsException(r.gameObject) ? exceptionMaterial : sceneMaterial;
            if (targetMaterial == null) continue;

            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = targetMaterial;

            r.sharedMaterials = mats;

            ParticleSystemRenderer psr = r as ParticleSystemRenderer;
            if (psr != null)
            {
                psr.sharedMaterial = targetMaterial;
                psr.trailMaterial = targetMaterial;
            }
        }
    }

    private bool IsException(GameObject go)
    {
        foreach (PlayerHealthLayerTag pair in exceptions)
        {
            if (pair == null) continue;
            if (go.layer != pair.layer) continue;

            string tag = string.IsNullOrEmpty(pair.tag) ? "Untagged" : pair.tag;
            if (go.tag == tag)
                return true;
        }
        return false;
    }

    public void RestoreToMax()
    {
        currentHealth = maxHealth;
        previousHealth = maxHealth;
        delayedDisplayHealth = maxHealth;

        delayedBarTimer = 0f;

        UpdateHealthBars();

        if (showDebugLogs)
            Debug.Log($"[PlayerHealth] Respawned - HP restored to {maxHealth}.");
    }

    private void OnGUI()
    {
        if (showDebugLogs)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 16;
            style.normal.textColor = new Color(0.4f, 1f, 0.4f);
            GUI.Label(new Rect(10, 140, 400, 30),
                $"[Player] HP: {currentHealth:F2}/{maxHealth} ({HealthPercentage:P0})  |  Hold R to heal", style);
        }
    }

    private void OnDisable()
    {
        if (!Free && gameObject.activeInHierarchy)
        {
            enabled = true;
            if (showDebugLogs)
                Debug.LogWarning("[PlayerHealth] OnDisable blocked. Script is locked (Free = false).");
        }
    }

    private void OnDestroy()
    {
        if (timeSlowed)
        {
            Time.timeScale = 1f;
            timeSlowed = false;
        }
    }
}

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(PlayerHealthLayerTag))]
public class PlayerHealthLayerTagDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        position = EditorGUI.PrefixLabel(position, label);

        int indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;

        SerializedProperty layer = property.FindPropertyRelative("layer");
        SerializedProperty tag = property.FindPropertyRelative("tag");

        float half = (position.width - 4f) * 0.5f;
        Rect layerRect = new Rect(position.x, position.y, half, position.height);
        Rect tagRect = new Rect(position.x + half + 4f, position.y, half, position.height);

        layer.intValue = EditorGUI.LayerField(layerRect, GUIContent.none, layer.intValue);
        tag.stringValue = EditorGUI.TagField(tagRect, GUIContent.none, tag.stringValue);

        EditorGUI.indentLevel = indent;
        EditorGUI.EndProperty();
    }
}
#endif