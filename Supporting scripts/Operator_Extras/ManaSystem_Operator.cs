using UnityEngine;
using System;

public class ManaSystem_Operator : MonoBehaviour
{
    [Header("Mana Reserves")]
    [Tooltip("Total mana reserves of the Operator (in Mezs)")]
    public float totalManaReserves = 500f;
    [Tooltip("Current amount of mana in total reserves")]
    [SerializeField] private float currentTotalMana = 500f;

    [Header("Instantaneously Usable Mana Reserves (IUMR)")]
    [Tooltip("Maximum capacity of IUMR")]
    public float iumrCapacity = 20f;
    [Tooltip("Current amount of mana in IUMR")]
    [SerializeField] private float currentIUMR = 20f;

    [Header("Mana Flow")]
    [Tooltip("Rate at which mana flows from Total Reserves into IUMR (Mezs per second)")]
    public float manaFlowRate = 4f;

    [Header("Debug")]
    [Tooltip("Show debug logs for mana usage")]
    public bool showDebugLogs = false;

    public event Action<float, float> OnIUMRChanged;
    public event Action<float, float> OnTotalManaChanged;
    public event Action OnManaEmpty;

    // Added to resolve the OperatorAI compile error
    public float CurrentMana => currentIUMR;
    public float maxMana => iumrCapacity;

    public float CurrentIUMR => currentIUMR;
    public float CurrentTotalMana => currentTotalMana;
    public float IUMRPercentage => currentIUMR / iumrCapacity;
    public float TotalManaPercentage => currentTotalMana / totalManaReserves;

    void Start()
    {
        currentTotalMana = totalManaReserves;
        currentIUMR = iumrCapacity;
        OnIUMRChanged?.Invoke(currentIUMR, iumrCapacity);
        OnTotalManaChanged?.Invoke(currentTotalMana, totalManaReserves);
    }

    void Update()
    {
        FlowManaIntoIUMR();
    }

    void FlowManaIntoIUMR()
    {
        if (currentIUMR < iumrCapacity && currentTotalMana > 0)
        {
            float flowAmount = manaFlowRate * Time.deltaTime;
            float availableSpace = iumrCapacity - currentIUMR;
            float actualFlow = Mathf.Min(flowAmount, availableSpace, currentTotalMana);
            currentTotalMana -= actualFlow;
            currentIUMR += actualFlow;
            OnIUMRChanged?.Invoke(currentIUMR, iumrCapacity);
            OnTotalManaChanged?.Invoke(currentTotalMana, totalManaReserves);
        }
    }

    public bool UseMana(float amount)
    {
        if (currentIUMR >= amount)
        {
            currentIUMR -= amount;
            OnIUMRChanged?.Invoke(currentIUMR, iumrCapacity);
            if (showDebugLogs)
                Debug.Log($"[Operator] Used {amount} Mezs. IUMR: {currentIUMR:F2}/{iumrCapacity}");
            if (currentIUMR <= 0.01f)
                OnManaEmpty?.Invoke();
            return true;
        }
        else
        {
            if (showDebugLogs)
                Debug.LogWarning($"[Operator] Insufficient IUMR! Needed {amount}, have {currentIUMR:F2}");
            return false;
        }
    }

    /// <summary>
    /// Forcefully drains IUMR without a sufficiency check. Use when the check
    /// has already been done externally (e.g. tendril attack).
    /// </summary>
    public void DrainIUMR(float amount)
    {
        currentIUMR = Mathf.Max(0f, currentIUMR - amount);
        OnIUMRChanged?.Invoke(currentIUMR, iumrCapacity);
        if (showDebugLogs)
            Debug.Log($"[Operator] Drained {amount} IUMR. IUMR: {currentIUMR:F2}/{iumrCapacity}");
        if (currentIUMR <= 0.01f)
            OnManaEmpty?.Invoke();
    }

    public bool HasEnoughMana(float amount)
    {
        return currentIUMR >= amount;
    }

    /// <summary>
    /// Restores mana reserves to maximum for ML episode resets.
    /// </summary>
    public void RestoreToMax()
    {
        currentTotalMana = totalManaReserves;
        currentIUMR = iumrCapacity;
        OnIUMRChanged?.Invoke(currentIUMR, iumrCapacity);
        OnTotalManaChanged?.Invoke(currentTotalMana, totalManaReserves);
    }

    void OnGUI()
    {
        if (showDebugLogs)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 16;
            style.normal.textColor = new Color(0.8f, 0.4f, 1f);
            GUI.Label(new Rect(10, 90, 400, 30),
                $"[OP] IUMR: {currentIUMR:F2}/{iumrCapacity} ({IUMRPercentage:P0})", style);
            GUI.Label(new Rect(10, 115, 400, 30),
                $"[OP] Reserves: {currentTotalMana:F2}/{totalManaReserves} ({TotalManaPercentage:P0})", style);
        }
    }
}