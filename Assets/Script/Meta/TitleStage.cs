using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 타이틀 3D 씬의 내 캐릭터 — 기본 모델(GameMeta) + 내 꾸미기. 빈 곳을 끌면 돌아감 (놓으면 천천히 앞으로).
/// 꾸미기 창에서 입어 보기(Preview) · 저장이 바뀌면 다시 입힘
/// </summary>
public class TitleStage : MonoBehaviour
{
    [Tooltip("캐릭터가 설 자리 (비우면 이 오브젝트)")]
    public Transform Spot;
    public float DragSpeed = 0.35f;
    [Tooltip("놓은 뒤 앞으로 돌아오는 속도 (0 = 안 돌아옴)")]
    public float ReturnSpeed = 1.5f;

    public static TitleStage Instance { get; private set; }

    GameObject _model;
    float _yaw;
    bool _drag;
    float _lastX;
    bool _previewing;

    void OnEnable()
    {
        Instance = this;
        PlayerSave.Changed += OnSaveChanged;
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        PlayerSave.Changed -= OnSaveChanged;
    }

    void Start() => Spawn();

    void Spawn()
    {
        if (_model != null) Destroy(_model);
        _model = CharacterPhoto.SpawnModel(Spot != null ? Spot : transform, PlayerSave.Look, gameObject.layer);
        var meta = GameMeta.Get();
        if (meta == null) Debug.LogWarning("[타이틀] Resources/GameMeta가 없어 — 캐릭터를 못 띄움");
        else if (meta.CharacterModel == null) Debug.LogWarning("[타이틀] GameMeta의 'Character Model'이 비어 있어 — 뼈대(Animator) 있는 기본 몸 모델을 넣어 줘");
        else if (_model != null && _model.GetComponentsInChildren<Renderer>(true).Length == 0)
            Debug.LogWarning("[타이틀] 캐릭터 모델에 보이는 메시가 없어 — 머리 · 몸이 따로면 CosmeticDatabase의 '머리' · '몸' 칸 Default Part에 넣어 줘", meta.CharacterModel);
    }

    void OnSaveChanged()
    {
        if (!_previewing) Wear(PlayerSave.Look);
    }

    /// <summary>입어 보기 (저장 안 함) — 꾸미기 창</summary>
    public void Preview(Appearance look)
    {
        _previewing = true;
        Wear(look);
    }

    /// <summary>입어 보기 끝 — 저장된 모습으로</summary>
    public void EndPreview()
    {
        _previewing = false;
        Wear(PlayerSave.Look);
    }

    void Wear(Appearance look)
    {
        if (_model == null) return;
        var ap = _model.GetComponent<CharacterAppearanceApplier>();
        if (ap != null) ap.Apply(look);
    }

    void Update()
    {
        if (_model == null) return;

        if (Input.GetMouseButtonDown(0))
        {
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            _drag = !overUi;
            _lastX = Input.mousePosition.x;
        }
        if (Input.GetMouseButtonUp(0)) _drag = false;

        if (_drag)
        {
            float x = Input.mousePosition.x;
            _yaw -= (x - _lastX) * DragSpeed;
            _lastX = x;
        }
        else if (ReturnSpeed > 0f)
            _yaw = Mathf.LerpAngle(_yaw, 0f, 1f - Mathf.Exp(-ReturnSpeed * Time.deltaTime));

        _model.transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
    }
}
