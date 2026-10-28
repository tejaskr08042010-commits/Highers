using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class MagicCircleHUD : MaskableGraphic
{
    [System.Serializable]
    public struct PointCoord
    {
        [Tooltip("X: 0 = on vertical axis, increases horizontally outward to 6")]
        public int x;
        [Tooltip("Y: 0 = on horizontal axis, increases vertically outward to 6")]
        public int y;
        public bool Equals(PointCoord o) => x == o.x && y == o.y;
    }

    [System.Serializable]
    public class ConnectionEntry
    {
        public PointCoord mainPoint;
        public List<PointCoord> connectTo = new List<PointCoord>();
    }

    [System.Serializable]
    public class FilledRegion
    {
        [Tooltip("Ordered points forming a closed filled polygon")]
        public List<PointCoord> points = new List<PointCoord>();
    }

    [System.Serializable]
    public class QuadrantSpell
    {
        public List<ConnectionEntry> connections = new List<ConnectionEntry>();
        public List<FilledRegion> filledRegions = new List<FilledRegion>();
    }

    private enum State
    {
        Idle,
        Open,
        Spreading,
        Revealing,
        Closing,
        Locked
    }

    [Header("References")]
    public Image ringImage;
    public BasicMovement_2 playerMovement;

    [Tooltip("The Operator boss. The Upper-Right spell can only be selected once it reaches Phase 3.")]
    public OperatorAI operatorAI;

    [HideInInspector] public bool magicSpells = true;

    [Header("Locked Spell Message")]
    [Tooltip("Text shown when the player tries to select the Upper-Right spell before Phase 3")]
    public Graphic thresholdText;
    [Tooltip("Seconds the text stays visible after the last click")]
    public float thresholdTextDuration = 2f;
    public float thresholdFadeInDuration = 0.1f;
    public float thresholdFadeOutDuration = 0.1f;

    [Header("Post Processing")]
    public Volume globalVolume;
    public float vignetteFadeDuration = 0.3f;

    [Header("Player Spells")]
    [Tooltip("The player GameObject that holds spell scripts")]
    public GameObject player;

    [Tooltip("Script name (exact class name) for the Top-Left spell")]
    public string spellNameTL;

    [Tooltip("Script name (exact class name) for the Top-Right spell")]
    public string spellNameTR;

    [Tooltip("Script name (exact class name) for the Bottom-Left spell")]
    public string spellNameBL;

    [Tooltip("Script name (exact class name) for the Bottom-Right spell")]
    public string spellNameBR;

    [Header("Spell Patterns")]
    public QuadrantSpell spellTL = new QuadrantSpell();
    public QuadrantSpell spellTR = new QuadrantSpell();
    public QuadrantSpell spellBL = new QuadrantSpell();
    public QuadrantSpell spellBR = new QuadrantSpell();

    [Header("Quadrant Symbols")]
    public Image imageTL;
    public Image imageTR;
    public Image imageBL;
    public Image imageBR;

    [Header("Circle Layout")]
    public float radiusFraction = 0.38f;
    public float innerFraction = 0.08f;
    public float outerFraction = 0.92f;

    [Header("Visuals")]
    [Tooltip("Show all points for coordinate mapping")]
    public bool showAllPoints = false;
    public Color dotColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    public Color lineColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    public Color fillColor = new Color(0.2f, 0.2f, 0.2f, 0.35f);
    public float dotRadius = 3f;
    public int dotSegments = 32;
    public float lineThickness = 1.5f;

    [Header("Animation")]
    public float lineAnimSpeed = 3f;
    public float hoverFadeSpeed = 4f;
    public float hudFadeSpeed = 4f;

    private State _state = State.Idle;
    private float _hudAlpha = 0f;
    private int _hoveredQuad = -1;
    private int _selectedQuad = -1;

    private float[] _hoverAlpha = new float[4];
    private List<float>[] _lineProgress = { new List<float>(), new List<float>(), new List<float>(), new List<float>() };

    private Vector2[][] _points = new Vector2[4][];
    private QuadrantSpell[] _spells;
    private Image[] _images;
    private string[] _spellNames;

    private float _glyphAlpha = 1f;
    private float _symbolAlpha = 0f;
    private float _phaseTimer = 0f;
    private int _lastActivatedQuad = -1;

    private bool _zWasReleased = false;
    private bool _spellActivated = false;

    private int _spellActiveBeforeOpen = -1;

    private float _thresholdTimer = 0f;
    private float _thresholdAlpha = 0f;

    private Vignette _vignette;
    private float _vignetteBaseIntensity;
    private float _vignetteFadeFrom;
    private float _vignetteFadeTo;
    private float _vignetteFadeTimer;
    private bool _vignetteFading;

    public bool IsFullyOpen => _state == State.Open;

    protected override void Start()
    {
        base.Start();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        _spells = new[] { spellTL, spellTR, spellBL, spellBR };
        _images = new[] { imageTL, imageTR, imageBL, imageBR };
        _spellNames = new[] { spellNameTL, spellNameTR, spellNameBL, spellNameBR };

        foreach (var img in _images)
            if (img != null) { var c = img.color; c.a = 0f; img.color = c; }

        SetThresholdTextAlpha(0f);

        if (player != null)
        {
            foreach (var name in _spellNames)
                SetSpellEnabled(name, false);
            SetSpellEnabled("BasicProjectiles", false);
        }

        if (globalVolume != null && globalVolume.profile.TryGet(out _vignette))
            _vignetteBaseIntensity = _vignette.intensity.value;
    }

    private void Update()
    {
        bool zHeld = Input.GetKey(KeyCode.Z);
        bool zJustPressed = Input.GetKeyDown(KeyCode.Z);
        bool zJustReleased = Input.GetKeyUp(KeyCode.Z);

        bool blockInput = _state == State.Open || _state == State.Spreading || _state == State.Revealing || _state == State.Closing;

        if (playerMovement != null)
        {
            playerMovement.noInput = blockInput;
            playerMovement.lockInput = false;
        }

        bool interactableState = _state == State.Open;
        Cursor.lockState = interactableState ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = interactableState;

        if (zJustPressed && _state == State.Idle && _zWasReleased)
        {
            _spellActiveBeforeOpen = _lastActivatedQuad;
            DisableAllSpellsAndProjectiles();
        }

        if (zJustReleased && (_state == State.Open || _state == State.Spreading))
            RestoreSpellAfterClose(_spellActiveBeforeOpen);

        switch (_state)
        {
            case State.Idle:
                if (!zHeld) _zWasReleased = true;
                if (zHeld && _zWasReleased)
                {
                    _state = State.Open;
                    _zWasReleased = false;
                    _glyphAlpha = 1f;
                    _symbolAlpha = 0f;
                    StartVignetteFade(0f);
                }
                _hudAlpha = Mathf.MoveTowards(_hudAlpha, 0f, hudFadeSpeed * Time.unscaledDeltaTime);
                break;

            case State.Open:
                if (!zHeld)
                {
                    _state = State.Idle;
                    ResetHover();
                    StartVignetteFade(_vignetteBaseIntensity);
                }
                _hudAlpha = Mathf.MoveTowards(_hudAlpha, 1f, hudFadeSpeed * Time.unscaledDeltaTime);
                BuildPoints();

                UpdateHover();
                UpdateLineAnimations();
                TrySelect();
                break;

            case State.Spreading:
                BuildPoints();
                UpdateHover();
                UpdateSpreadAnimations();
                _symbolAlpha = Mathf.MoveTowards(_symbolAlpha, 0.3f, hoverFadeSpeed * Time.unscaledDeltaTime);
                if (AllQuadrantsComplete())
                {
                    _symbolAlpha = 0.3f;
                    _state = State.Revealing;
                    _phaseTimer = 0f;
                    _spellActivated = false;
                }
                break;

            case State.Revealing:
                UpdateHover();
                _phaseTimer += Time.unscaledDeltaTime;
                if (!_spellActivated && _phaseTimer >= 0.3f)
                {
                    _spellActivated = true;
                    ActivateSelectedSpell();
                    _state = State.Closing;
                    _phaseTimer = 0f;
                    StartVignetteFade(_vignetteBaseIntensity);
                }
                break;

            case State.Closing:
                UpdateHover();
                _phaseTimer += Time.unscaledDeltaTime;
                float ct = Mathf.Clamp01(_phaseTimer / 0.2f);
                _glyphAlpha = 1f - ct;
                _symbolAlpha = 0.3f * (1f - ct);
                _hudAlpha = 1f - ct;
                if (ct >= 1f)
                {
                    _state = State.Locked;
                    _hudAlpha = 0f;
                    _symbolAlpha = 0f;
                    _glyphAlpha = 1f;
                    _zWasReleased = false;
                    ResetAll();
                }
                break;

            case State.Locked:
                if (!zHeld) _zWasReleased = true;
                if (_zWasReleased) _state = State.Idle;
                _hudAlpha = 0f;
                break;
        }

        if (ringImage != null)
        {
            Color c = ringImage.color;
            c.a = _hudAlpha;
            ringImage.color = c;
        }

        UpdateSymbolImages();
        UpdateThresholdText();
        UpdateVignetteFade();
        SetVerticesDirty();
    }

    private void UpdateThresholdText()
    {
        if (thresholdText == null) return;

        _thresholdTimer = Mathf.Max(0f, _thresholdTimer - Time.unscaledDeltaTime);

        bool show = _thresholdTimer > 0f;
        float duration = show ? thresholdFadeInDuration : thresholdFadeOutDuration;
        float speed = duration > 0.0001f ? 1f / duration : 1000f;

        _thresholdAlpha = Mathf.MoveTowards(_thresholdAlpha, show ? 1f : 0f, speed * Time.unscaledDeltaTime);
        SetThresholdTextAlpha(_thresholdAlpha);
    }

    private void SetThresholdTextAlpha(float a)
    {
        if (thresholdText == null) return;
        Color c = thresholdText.color;
        c.a = a;
        thresholdText.color = c;
    }

    private void StartVignetteFade(float target)
    {
        if (_vignette == null) return;
        _vignetteFadeFrom = _vignette.intensity.value;
        _vignetteFadeTo = target;
        _vignetteFadeTimer = 0f;
        _vignetteFading = true;
    }

    private void UpdateVignetteFade()
    {
        if (!_vignetteFading || _vignette == null) return;
        _vignetteFadeTimer += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(_vignetteFadeTimer / vignetteFadeDuration);
        _vignette.intensity.value = Mathf.Lerp(_vignetteFadeFrom, _vignetteFadeTo, t);
        if (t >= 1f) _vignetteFading = false;
    }

    private void ActivateSelectedSpell()
    {
        if (player == null) return;
        _lastActivatedQuad = _selectedQuad;
        for (int q = 0; q < 4; q++)
            SetSpellEnabled(_spellNames[q], q == _selectedQuad);
        SetSpellEnabled("BasicProjectiles", true);
    }

    private void DisableAllSpellsAndProjectiles()
    {
        if (player == null) return;
        foreach (var name in _spellNames)
            SetSpellEnabled(name, false);
        SetSpellEnabled("BasicProjectiles", false);
    }

    private void RestoreSpellAfterClose(int quadIndex)
    {
        if (player == null) return;
        SetSpellEnabled("BasicProjectiles", true);
        if (quadIndex < 0 || quadIndex >= _spellNames.Length) return;
        SetSpellEnabled(_spellNames[quadIndex], true);
    }

    private void SetSpellEnabled(string scriptName, bool enabled)
    {
        if (player == null || string.IsNullOrEmpty(scriptName)) return;
        foreach (var mb in player.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            if (mb.GetType().Name == scriptName)
            {
                mb.enabled = enabled;
                return;
            }
        }
        Debug.LogWarning($"[MagicCircleHUD] Script '{scriptName}' not found on player '{player.name}'.");
    }

    private void TrySelect()
    {
        if (_state != State.Open) return;
        if (!Input.GetMouseButtonDown(0)) return;
        if (_hoveredQuad < 0) return;

        if (_hoveredQuad == 1 && (operatorAI == null || !operatorAI.phase3))
        {
            _thresholdTimer = thresholdTextDuration;
            return;
        }

        var prog = _lineProgress[_hoveredQuad];
        if (prog.Count == 0) return;

        _selectedQuad = _hoveredQuad;
        _state = State.Spreading;
        _symbolAlpha = 0f;

        var selectedSpell = _spells[_selectedQuad];
        int lineCount = GetUniqueLines(selectedSpell).Count;
        for (int q = 0; q < 4; q++)
        {
            if (q == _selectedQuad) continue;
            _lineProgress[q] = new List<float>(new float[lineCount]);
        }
    }

    private void UpdateSpreadAnimations()
    {
        bool selectedComplete = true;
        if (_selectedQuad >= 0 && _lineProgress[_selectedQuad] != null)
        {
            var selProg = _lineProgress[_selectedQuad];
            for (int i = 0; i < selProg.Count; i++)
            {
                selProg[i] = Mathf.MoveTowards(selProg[i], 1f, lineAnimSpeed * Time.unscaledDeltaTime);
                if (selProg[i] < 0.999f) selectedComplete = false;
            }
        }

        for (int q = 0; q < 4; q++)
        {
            _hoverAlpha[q] = Mathf.MoveTowards(_hoverAlpha[q], 1f, hoverFadeSpeed * Time.unscaledDeltaTime);
            if (q == _selectedQuad) continue;

            if (!selectedComplete) continue;

            for (int i = 0; i < _lineProgress[q].Count; i++)
                _lineProgress[q][i] = Mathf.MoveTowards(_lineProgress[q][i], 1f, lineAnimSpeed * Time.unscaledDeltaTime);
        }
    }

    private bool AllQuadrantsComplete()
    {
        for (int q = 0; q < 4; q++)
        {
            foreach (var p in _lineProgress[q])
                if (p < 0.999f) return false;
        }
        return true;
    }

    private void UpdateHover()
    {
        if (_state != State.Open)
        {
            _hoveredQuad = -1;
            return;
        }

        Vector2 localMouse;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform, Input.mousePosition, null, out localMouse);

        int newHover = -1;

        float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * radiusFraction;
        if (localMouse.magnitude <= radius)
        {
            if (localMouse.x < 0 && localMouse.y > 0) newHover = 0;
            else if (localMouse.x > 0 && localMouse.y > 0) newHover = 1;
            else if (localMouse.x < 0 && localMouse.y < 0) newHover = 2;
            else if (localMouse.x > 0 && localMouse.y < 0) newHover = 3;
        }

        if (newHover != _hoveredQuad)
        {
            if (_hoveredQuad >= 0)
                _lineProgress[_hoveredQuad] = new List<float>();
            if (newHover >= 0 && _spells[newHover] != null)
            {
                int count = GetUniqueLines(_spells[newHover]).Count;
                _lineProgress[newHover] = new List<float>(new float[count]);
            }
            _hoveredQuad = newHover;
        }

        for (int q = 0; q < 4; q++)
            _hoverAlpha[q] = Mathf.MoveTowards(_hoverAlpha[q], q == _hoveredQuad ? 1f : 0f, hoverFadeSpeed * Time.unscaledDeltaTime);
    }

    private void UpdateLineAnimations()
    {
        for (int q = 0; q < 4; q++)
        {
            if (_hoverAlpha[q] <= 0.001f) continue;
            for (int i = 0; i < _lineProgress[q].Count; i++)
                _lineProgress[q][i] = Mathf.MoveTowards(_lineProgress[q][i], 1f, lineAnimSpeed * Time.unscaledDeltaTime);
        }
    }

    private void UpdateSymbolImages()
    {
        for (int q = 0; q < 4; q++)
        {
            if (_images[q] == null) continue;
            Color c = _images[q].color;

            if (_state == State.Spreading || _state == State.Revealing || _state == State.Closing)
                c.a = (q == _selectedQuad) ? _symbolAlpha : 0f;
            else
                c.a = 0f;

            _images[q].color = c;
        }
    }

    private void ResetHover()
    {
        _hoveredQuad = -1;
        for (int q = 0; q < 4; q++)
        {
            _hoverAlpha[q] = 0f;
            _lineProgress[q] = new List<float>();
        }
    }

    private void ResetAll()
    {
        _selectedQuad = -1;
        _glyphAlpha = 1f;
        _symbolAlpha = 0f;
        ResetAll_Hover();
    }

    private void ResetAll_Hover()
    {
        _hoveredQuad = -1;
        for (int q = 0; q < 4; q++)
        {
            _hoverAlpha[q] = 0f;
            _lineProgress[q] = new List<float>();
        }
    }

    private void BuildPoints()
    {
        float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * radiusFraction;
        float innerR = radius * innerFraction;
        float outerR = radius * outerFraction;
        float step = (outerR - innerR) / 7f;

        for (int q = 0; q < 4; q++)
        {
            _points[q] = new Vector2[28];
            int idx = 0;
            float sx = (q == 1 || q == 3) ? 1f : -1f;
            float sy = (q == 0 || q == 1) ? 1f : -1f;

            for (int y = 0; y <= 6; y++)
            {
                float distY = (6 - y + 0.5f) * step;
                for (int x = 0; x <= y; x++)
                {
                    float distX = (x + 0.5f) * step;
                    _points[q][idx++] = new Vector2(sx * distX, sy * distY);
                }
            }
        }
    }

    private List<(PointCoord, PointCoord)> GetUniqueLines(QuadrantSpell spell)
    {
        var result = new List<(PointCoord, PointCoord)>();
        if (spell?.connections == null) return result;
        foreach (var entry in spell.connections)
        {
            if (entry.connectTo == null) continue;
            foreach (var target in entry.connectTo)
            {
                bool dup = false;
                foreach (var ex in result)
                    if ((ex.Item1.Equals(entry.mainPoint) && ex.Item2.Equals(target)) ||
                        (ex.Item1.Equals(target) && ex.Item2.Equals(entry.mainPoint)))
                    { dup = true; break; }
                if (!dup) result.Add((entry.mainPoint, target));
            }
        }
        return result;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (_hudAlpha <= 0.001f && _state != State.Revealing) return;

        float scaledThickness = GetPixelThickness(lineThickness);
        float scaledDot = GetPixelThickness(dotRadius);
        float ga = _glyphAlpha * _hudAlpha;

        for (int q = 0; q < 4; q++)
        {
            if (_points[q] == null) continue;

            if (showAllPoints)
                foreach (var p in _points[q])
                    AddDot(vh, p, scaledDot, dotSegments, WithAlpha(dotColor, ga));

            QuadrantSpell spell = (_state == State.Spreading || _state == State.Revealing || _state == State.Closing)
                ? _spells[_selectedQuad]
                : _spells[q];

            float ha = _hoverAlpha[q] * ga;
            if (ha <= 0.001f) continue;
            if (spell == null) continue;

            var seen = new HashSet<(int, int)>();
            foreach (var entry in spell.connections)
            {
                if (seen.Add((entry.mainPoint.x, entry.mainPoint.y)))
                    AddDot(vh, GetPoint(q, entry.mainPoint.x, entry.mainPoint.y), scaledDot, dotSegments, WithAlpha(dotColor, ha));
                if (entry.connectTo == null) continue;
                foreach (var pt in entry.connectTo)
                    if (seen.Add((pt.x, pt.y)))
                        AddDot(vh, GetPoint(q, pt.x, pt.y), scaledDot, dotSegments, WithAlpha(dotColor, ha));
            }

            var lines = GetUniqueLines(spell);
            for (int i = 0; i < lines.Count; i++)
            {
                float progress = i < _lineProgress[q].Count ? _lineProgress[q][i] : 0f;
                if (progress <= 0.001f) continue;
                Vector2 a = GetPoint(q, lines[i].Item1.x, lines[i].Item1.y);
                Vector2 b = GetPoint(q, lines[i].Item2.x, lines[i].Item2.y);
                Vector2 mid = (a + b) * 0.5f;
                AddLine(vh, a, Vector2.Lerp(a, mid, progress), WithAlpha(lineColor, ha), scaledThickness);
                AddLine(vh, b, Vector2.Lerp(b, mid, progress), WithAlpha(lineColor, ha), scaledThickness);
            }

            if (spell.filledRegions == null) continue;
            foreach (var poly in spell.filledRegions)
            {
                if (poly.points == null || poly.points.Count < 3) continue;
                var verts = new Vector2[poly.points.Count];
                for (int i = 0; i < poly.points.Count; i++)
                    verts[i] = GetPoint(q, poly.points[i].x, poly.points[i].y);
                AddPolygon(vh, verts, WithAlpha(fillColor, ha));
            }
        }
    }

    private float GetPixelThickness(float desiredScreenPixels)
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        float scaleFactor = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
        return Mathf.Max(1.5f, desiredScreenPixels / scaleFactor);
    }

    private Vector2 GetPoint(int q, int x, int y)
    {
        int oy = 6 - y;
        int ox = x;
        int idx = oy * (oy + 1) / 2 + ox;
        if (_points[q] == null || idx < 0 || idx >= _points[q].Length) return Vector2.zero;
        return _points[q][idx];
    }

    private Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

    private void AddLine(VertexHelper vh, Vector2 a, Vector2 b, Color col, float thickness)
    {
        if ((b - a).sqrMagnitude < 0.0001f) return;
        a.x = Mathf.Round(a.x); a.y = Mathf.Round(a.y);
        b.x = Mathf.Round(b.x); b.y = Mathf.Round(b.y);
        Vector2 dir = (b - a).normalized;
        Vector2 perp = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);
        int idx = vh.currentVertCount;
        AddVert(vh, a - perp, col);
        AddVert(vh, a + perp, col);
        AddVert(vh, b + perp, col);
        AddVert(vh, b - perp, col);
        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx, idx + 2, idx + 3);
    }

    private void AddDot(VertexHelper vh, Vector2 centre, float radius, int segs, Color col)
    {
        float h = radius;
        int idx = vh.currentVertCount;
        AddVert(vh, centre + new Vector2(-h, -h), col);
        AddVert(vh, centre + new Vector2(h, -h), col);
        AddVert(vh, centre + new Vector2(h, h), col);
        AddVert(vh, centre + new Vector2(-h, h), col);
        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx, idx + 2, idx + 3);
    }

    private void AddPolygon(VertexHelper vh, Vector2[] verts, Color col)
    {
        int start = vh.currentVertCount;
        foreach (var v in verts) AddVert(vh, v, col);
        for (int i = 1; i < verts.Length - 1; i++)
            vh.AddTriangle(start, start + i, start + i + 1);
    }

    private void AddVert(VertexHelper vh, Vector2 pos, Color col)
    {
        pos.x = Mathf.Round(pos.x);
        pos.y = Mathf.Round(pos.y);
        var v = UIVertex.simpleVert;
        v.position = pos;
        v.color = col;
        vh.AddVert(v);
    }
}