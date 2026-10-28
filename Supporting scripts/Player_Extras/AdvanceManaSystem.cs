using UnityEngine;
using UnityEngine.UI;
using System;

public class AdvanceManaSystem : MonoBehaviour
{
    [Header("Mana Reserves")]
    [Tooltip("Total mana reserves the demon possesses (in Mezs)")]
    public float totalManaReserves = 1000f;

    [Tooltip("Current amount of mana in total reserves")]
    [SerializeField] private float currentTotalMana = 1000f;

    [Header("Instantaneously Usable Mana Reserves (IUMR)")]
    [Tooltip("Maximum capacity of IUMR - mana that can be used instantly (in Mezs)")]
    public float iumrCapacity = 10f;

    [Tooltip("Current amount of mana in IUMR")]
    [SerializeField] private float currentIUMR = 10f;

    [Header("Mana Flow")]
    [Tooltip("Rate at which mana flows from Total Reserves into IUMR (Mezs per second)")]
    public float manaFlowRate = 2f;

    [Header("Regeneration (Optional)")]
    [Tooltip("Enable passive regeneration of total mana reserves")]
    public bool enableManaRegeneration = false;

    [Tooltip("Rate of total mana regeneration (Mezs per second)")]
    public float manaRegenerationRate = 0.5f;

    [Header("Mana UI Bars")]
    [Tooltip("UI Image set to Type: Filled, Method: Horizontal. Displays current IUMR.")]
    public Image iumrBar;
    [Tooltip("UI Image set to Type: Filled, Method: Horizontal. Displays Total Reserves.")]
    public Image totalManaBar;

    [Header("Debug")]
    [Tooltip("Show debug logs for mana usage")]
    public bool showDebugLogs = false;

    public event Action<float, float> OnIUMRChanged;
    public event Action<float, float> OnTotalManaChanged;
    public event Action OnManaEmpty;

    public float CurrentIUMR => currentIUMR;
    public float CurrentTotalMana => currentTotalMana;
    public float IUMRPercentage => currentIUMR / iumrCapacity;
    public float TotalManaPercentage => currentTotalMana / totalManaReserves;

    private bool manaFlowFrozen = false;

    void Start()
    {
        currentTotalMana = totalManaReserves;
        currentIUMR = iumrCapacity;

        UpdateManaBars();

        OnIUMRChanged?.Invoke(currentIUMR, iumrCapacity);
        OnTotalManaChanged?.Invoke(currentTotalMana, totalManaReserves);
    }

    void Update()
    {
        if (!manaFlowFrozen)
        {
            FlowManaIntoIUMR();
        }

        if (enableManaRegeneration)
        {
            RegenerateTotalMana();
        }

        UpdateManaBars();
    }

    private void UpdateManaBars()
    {
        if (iumrBar != null)
            iumrBar.fillAmount = IUMRPercentage;

        if (totalManaBar != null)
            totalManaBar.fillAmount = TotalManaPercentage;
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

    void RegenerateTotalMana()
    {
        if (currentTotalMana < totalManaReserves)
        {
            float regenAmount = manaRegenerationRate * Time.deltaTime;
            currentTotalMana = Mathf.Min(currentTotalMana + regenAmount, totalManaReserves);

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
                Debug.Log($"Used {amount} Mezs. IUMR: {currentIUMR:F2}/{iumrCapacity}");

            if (currentIUMR <= 0.01f)
                OnManaEmpty?.Invoke();

            return true;
        }
        else
        {
            if (showDebugLogs)
                Debug.LogWarning($"Insufficient IUMR! Needed {amount} Mezs, only have {currentIUMR:F2}");

            return false;
        }
    }

    public bool HasEnoughMana(float amount)
    {
        return currentIUMR >= amount;
    }

    public void DrainIUMR(float amount)
    {
        currentIUMR = Mathf.Max(0f, currentIUMR - amount);
        OnIUMRChanged?.Invoke(currentIUMR, iumrCapacity);

        if (showDebugLogs)
            Debug.Log($"IUMR drained by {amount}. IUMR: {currentIUMR:F2}/{iumrCapacity}");

        if (currentIUMR <= 0.01f)
            OnManaEmpty?.Invoke();
    }

    public float DrainAllIUMR()
    {
        float drained = currentIUMR;
        currentIUMR = 0f;
        OnIUMRChanged?.Invoke(currentIUMR, iumrCapacity);
        OnManaEmpty?.Invoke();

        if (showDebugLogs)
            Debug.Log($"All IUMR drained. Drained: {drained:F2} Mezs");

        return drained;
    }

    public void SetManaFlowFrozen(bool frozen)
    {
        manaFlowFrozen = frozen;

        if (showDebugLogs)
            Debug.Log($"Mana flow frozen: {frozen}");
    }

    public void ForceRefillIUMR()
    {
        float refillAmount = Mathf.Min(iumrCapacity - currentIUMR, currentTotalMana);
        currentTotalMana -= refillAmount;
        currentIUMR += refillAmount;

        OnIUMRChanged?.Invoke(currentIUMR, iumrCapacity);
        OnTotalManaChanged?.Invoke(currentTotalMana, totalManaReserves);

        if (showDebugLogs)
            Debug.Log($"Force refilled IUMR! IUMR: {currentIUMR:F2}/{iumrCapacity}");
    }

    public void AddManaToReserves(float amount)
    {
        currentTotalMana = Mathf.Min(currentTotalMana + amount, totalManaReserves);
        OnTotalManaChanged?.Invoke(currentTotalMana, totalManaReserves);

        if (showDebugLogs)
            Debug.Log($"Added {amount} Mezs to reserves. Total: {currentTotalMana:F2}/{totalManaReserves}");
    }

    public void IncreaseMaxMana(float amount)
    {
        totalManaReserves += amount;
        currentTotalMana += amount;

        OnTotalManaChanged?.Invoke(currentTotalMana, totalManaReserves);

        if (showDebugLogs)
            Debug.Log($"Increased max mana by {amount}. New max: {totalManaReserves}");
    }

    public void IncreaseIUMRCapacity(float amount)
    {
        iumrCapacity += amount;

        OnIUMRChanged?.Invoke(currentIUMR, iumrCapacity);

        if (showDebugLogs)
            Debug.Log($"Increased IUMR capacity by {amount}. New capacity: {iumrCapacity}");
    }

    public void IncreaseManaFlowRate(float amount)
    {
        manaFlowRate += amount;

        if (showDebugLogs)
            Debug.Log($"Increased mana flow rate by {amount}. New rate: {manaFlowRate} Mezs/s");
    }

    public void DrainTotalReserves(float amount)
    {
        float drained = Mathf.Min(amount, currentTotalMana);
        currentTotalMana = Mathf.Max(0f, currentTotalMana - drained);
        OnTotalManaChanged?.Invoke(currentTotalMana, totalManaReserves);
    }

    void OnGUI()
    {
        if (showDebugLogs)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 16;
            style.normal.textColor = Color.white;

            GUI.Label(new Rect(10, 10, 400, 30),
                $"IUMR: {currentIUMR:F2}/{iumrCapacity} Mezs ({IUMRPercentage:P0})", style);
            GUI.Label(new Rect(10, 35, 400, 30),
                $"Total Reserves: {currentTotalMana:F2}/{totalManaReserves} Mezs ({TotalManaPercentage:P0})", style);
            GUI.Label(new Rect(10, 60, 400, 30),
                $"Flow Rate: {manaFlowRate} Mezs/s | Flow Frozen: {manaFlowFrozen}", style);
        }
    }
}