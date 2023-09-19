using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Tilemaps;
using Debug = UnityEngine.Debug;

[Serializable]
public struct InputValues
{
    public float horizontal,   vertical;
    public bool  jumpDown,     jumpUp;
    public bool  primarySkill, secondarySkill, utilitySkill, ultimateSkill;
    public bool  specialSkill, itemSkill;
    public bool  interact;
}

public class PlayerController : MonoBehaviour
{
    private const float JUMP_GRACE_TIME = 0.2f;
    private const float JUMP_BUFFER     = 0.2f;

    [SerializeField] private PlayerInput playerInput;

    [Inject]         private MapManager _mapManager;
    [SerializeField] private Tilemap    ladderTilemap;
    [SerializeField] private Tilemap    floorTilemap;

    private Player    _player;
    public  Transform attackPosTransform;

    [SerializeField] private InputValues input;

    private Rigidbody2D   _rigidbody2D;
    private BoxCollider2D _boxCollider2D;

    public Rigidbody2D Rigidbody2D => _rigidbody2D;

    public PlayerStats playerStats;

    private int      _jumpCountOffset; //점프 카운트를 조정하기 위한 변수
    private JumpDire _jumpDirection;
    private JumpDire _lastLadderJumpDirection;

    private float Speed        => playerStats.Speed;
    private float JumpHeight   => playerStats.JumpHeight;
    private int   MaxJumpCount => playerStats.MaxJumpCount + _jumpCountOffset;

    public Facing PlayerFacing => transform.localScale.x > 0 ?
        Facing.Left :
        Facing.Right;

    public bool Controllable { get; private set; } = true;

    public enum Facing
    {
        Left  = -1,
        Right = 1
    }

    private void Awake()
    {
        _rigidbody2D   = GetComponent<Rigidbody2D>();
        _boxCollider2D = GetComponent<BoxCollider2D>();
        playerInput    = GetComponent<PlayerInput>();

        attackPosTransform = transform.Find("AttackPoint") ?? transform;


        DIContainer.Inject(this);
        Initialize(_mapManager.GetMap(MapType.Normal));
    }

    private void OnEnable()
    {
        EventBus.Subscribe<MapChangedEvent>(OnMapChanged);
        InitializePlayerInput();
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<MapChangedEvent>(OnMapChanged);
        RemovePlayerInput();
    }

    /// <summary>
    /// 맵이 변경되면 해당 맵에 있는 타일맵을 가져와서 저장한다.
    /// TODO: 그리고 해당 맵의 무작위 위치로 플레이어를 옮기도록 해야함
    /// </summary>
    private void OnMapChanged(MapChangedEvent _obj) => Initialize(_obj.mapData.currentMap);

    private void Initialize(PlayMap _currentMap)
    {
        ladderTilemap = _currentMap.GetTilemap("Ladder");
        floorTilemap  = _currentMap.GetTilemap("Map");
    }

    private void Update()
    {
        // if (player.clientID != GameManager.Instance.playerID)
        //     return;
        //멀티 플레이어 환경

        var _position = transform.position;
        jumpGraceTimer -= Time.deltaTime;
        jumpBuffer     -= Time.deltaTime;

        // GetInput(); //Key Input 값 받아오기

        // //점프중일때(상승) 바닥을 뚫고 올라갈 수 있게 해준다. (Obsoleted - Collider 안에서 점프 못하게 막았음)
        _boxCollider2D.isTrigger = climbLadder;

        //사다리를 타고있으면 중력 영향을 받지않게 해준다.
        _rigidbody2D.gravityScale = climbLadder ?
            0 :
            3;

        //공중점프 제한 및 코요테타임 구현용
        //코요테 타임은 바닥에 붙어있을때 언제나 일정 수준을 유지하므로
        //jumpGraceTimer가 0이면 공중에 떠있는 상태이므로, 최대 점프 횟수를 -1 조정해준다.
        _jumpCountOffset = jumpGraceTimer <= 0 ?
            -1 :
            0;

        ClimbLadder(_position);

        Jump();

        {
            //함수화 해야함
            {
                if (Mathf.Abs(_rigidbody2D.velocity.y) > 0.001f)
                    _player._animator.SetBool("IsJump", true);
            }
        }

        ClampVelocity();

        ResetJump();

        _jumpDirection = GetNowJumpDirection();

        CheckInteraction();

        UseSkill();
    }

    private void InitializePlayerInput()
    {
        playerInput.actions["HorizontalMove"].performed += OnHorizontalMove;
        playerInput.actions["VerticalMove"].performed   += OnVerticalMove;
        playerInput.actions["Jump"].performed           += OnJump;
        playerInput.actions["Special"].performed        += OnSpecial;
        playerInput.actions["Primary"].performed        += OnPrimary;
        playerInput.actions["Secondary"].performed      += OnSecondary;
        playerInput.actions["Utility"].performed        += OnUtility;
        playerInput.actions["Ultimate"].performed       += OnUltimate;
        playerInput.actions["UseItem"].performed        += OnUseItem;
        playerInput.actions["Interact"].performed       += OnInteract;
    }

    private void RemovePlayerInput()
    {
        playerInput.actions["HorizontalMove"].performed -= OnHorizontalMove;
        playerInput.actions["VerticalMove"].performed   -= OnVerticalMove;
        playerInput.actions["Jump"].performed           -= OnJump;
        playerInput.actions["Special"].performed        -= OnSpecial;
        playerInput.actions["Primary"].performed        -= OnPrimary;
        playerInput.actions["Secondary"].performed      -= OnSecondary;
        playerInput.actions["Utility"].performed        -= OnUtility;
        playerInput.actions["Ultimate"].performed       -= OnUltimate;
        playerInput.actions["UseItem"].performed        -= OnUseItem;
        playerInput.actions["Interact"].performed       -= OnInteract;
    }

    public PlayerController Initialize(Player _initPlayer)
    {
        _player = _initPlayer;
        return this;
    }

    public void OnHorizontalMove(InputAction.CallbackContext _context) => input.horizontal = _context.ReadValue<float>();
    public void OnVerticalMove(InputAction.CallbackContext   _context) => input.vertical = _context.ReadValue<float>();

    public async void OnJump(InputAction.CallbackContext _context)
    {
        await UniTask.Yield();                            //이 기능은 유니티 Update에서 실행되지 않으므로 UniTask.Yield()를 통해 다음 프레임까지 대기해준다.
        input.jumpDown = _context.ReadValue<float>() > 0; //유니티 업데이트 타임때 값을 업데이트 해주고
        await UniTask.Yield();                            //다음프레임에
        input.jumpDown = false;                           //초기화
    }

    public void OnSpecial(InputAction.CallbackContext   _context) => input.specialSkill = _context.ReadValue<float>()   > 0;
    public void OnPrimary(InputAction.CallbackContext   _context) => input.primarySkill = _context.ReadValue<float>()   > 0;
    public void OnSecondary(InputAction.CallbackContext _context) => input.secondarySkill = _context.ReadValue<float>() > 0;
    public void OnUtility(InputAction.CallbackContext   _context) => input.utilitySkill = _context.ReadValue<float>()   > 0;
    public void OnUltimate(InputAction.CallbackContext  _context) => input.ultimateSkill = _context.ReadValue<float>()  > 0;
    public void OnUseItem(InputAction.CallbackContext   _context) => input.itemSkill = _context.ReadValue<float>()      > 0;

    public async void OnInteract(InputAction.CallbackContext _context)
    {
        await UniTask.Yield();  
        input.interact = _context.ReadValue<float>() > 0;
        await UniTask.Yield();  
        input.interact = false;
    }

    // public async void OnInteract(InputAction.CallbackContext  _context)
    // {
    //     await UniTask.Yield();
    //     input.interact = _context.ReadValue<float>() > 0;
    //     await UniTask.Yield();
    //     input.interact = false;
    // }

    private void CheckInteraction()
    {
        if (!input.interact) return;

        foreach (Collider2D _collider in Physics2D.OverlapCircleAll(transform.position, 1f, LayerMask.GetMask("Interactable")))
            if (_collider.TryGetComponent(out InteractableObject _interactableObject))
                _interactableObject.Interact();
    }

    private void FixedUpdate()
    {
        if (climbLadder) //사다리에 타고있으면 좌우 이동 막기
            return;

        Move();
    }

    //바닥 착지시 실행할 Action
    private Action _landingAction;

    public void AddLandingAction(Action _pAction)
    {
        _landingAction += _pAction;
    }

    private void OnCollisionEnter2D(Collision2D _pOther)
    {
        if (_pOther.gameObject.layer == LayerMask.NameToLayer("Floor"))
        {
            if (jumpBuffer > 0) //바닥에 닿았는데 찰나의 차이로 점프를 미리 시도했다면 점프를 실행해준다.
            {
                Debug.Log("Buffered Jump");
                jumpCount = 0;
                Jump(true);
            }

            //landingAction 실행후 초기화(바닥에 착지시 실행할 Action)
            _landingAction?.Invoke();
            _landingAction = null;
        }
    }

#region Input

    private void GetInput()
    {
#if UNITY_EDITOR
        input.horizontal     = Input.GetAxisRaw("Horizontal");
        input.vertical       = Input.GetAxisRaw("Vertical");
        input.jumpDown       = Input.GetButtonDown("Jump");
        input.jumpUp         = Input.GetButtonUp("Jump");
        input.primarySkill   = Input.GetButton("PrimarySkill");
        input.secondarySkill = Input.GetButton("SecondarySkill");
        input.utilitySkill   = Input.GetButton("MovementSkill");
        input.ultimateSkill  = Input.GetButton("UltimateSkill");
        input.specialSkill   = Input.GetButton("SpecialSkill");
        input.itemSkill      = Input.GetButton("ItemSkill");

#elif UNITY_ANDROID
        input.horizontal = VJoystick.MovementDirection;

#endif
    }

#endregion

#region MoveAction

    private void Move()
    {
        if (!Controllable) return;

        _rigidbody2D.velocity = new(input.horizontal * Speed, _rigidbody2D.velocity.y);
        _player._animator.SetBool("IsWalk", input.horizontal != 0);
        FlipSprite();
    }

    //이동 방향에 따라서 스프라이트를 뒤집어준다.
    private void FlipSprite()
    {
        Transform _transformCache = transform;

        _transformCache.localScale = input.horizontal switch
        {
            > 0 => new(-1, 1, 1),
            < 0 => new(1, 1, 1),
            _   => _transformCache.localScale
        };
    }

#endregion

#region JumpAction

    [DoNotSerialize] private readonly RaycastHit2D[] _tmpVar = new RaycastHit2D[1];

    [SerializeField] private int   jumpCount;
    [SerializeField] private float jumpBuffer;     //Buffering Time
    [SerializeField] private float jumpGraceTimer; //Coyote Time

    private void Jump(bool _forced = false)
    {
        if (!_forced) //점프키를 무시하는 강제점프가 아니라면
            if (!input.jumpDown)
                return; //점프키 상태 확인

        if (!Controllable) return;

        //사다리를 타고 있을때 벽 안에서 점프를 하지 못하게 막는다.
        if (Physics2D.RaycastNonAlloc(transform.position, Vector2.up, _tmpVar, .1f, LayerMask.GetMask("Floor")) > 0)
        {
            Debug.Log($"WallJump - {_tmpVar[0].collider.name}");
            return;
        }

        //점프키가 눌러졌으므로, 점프 버퍼는 채워준다.
        jumpBuffer = JUMP_BUFFER;

        //최대 점프 횟수에 도달하면 점프하지 않는다.
        if (jumpCount >= MaxJumpCount)
        {
            Debug.Log($"Max Jump Count - {jumpCount} / {MaxJumpCount}");
            return;
        }

        //사다리에서 아래방향 점프를 했다면 아래 방향으로 떨어뜨려준다.
        //=>내리려는 의도로 점프를 했을때 작동
        var _jumpHeight = climbLadder && input.vertical < 0 ?
            -JumpHeight * .5f :
            JumpHeight;

        jumpBuffer = 0; //점프 했으므로 버퍼 초기화
        jumpCount++;    //점프횟수 증가

        //사다리를 타고있었다면 사다리 타기를 종료한다.
        if (climbLadder)
        {
            _player._animator.speed = 1;
            climbLadder             = false;
            _player._animator.SetBool("IsClimb", false);

            _previousLadderPos = ladderPos.x;
        }

        //실제 점프 및 점프관련 애니메이션 실행
        _rigidbody2D.velocity = new(_rigidbody2D.velocity.x, _jumpHeight);
        Animation.PlayAnimation(_player._animator, "Jump");
        _player._animator.SetBool("IsJump", true);

        if (jumpGraceTimer > 0)              //정상적으로 점프를 했다면
            jumpGraceTimer = float.MaxValue; //점프 횟수 조정을 받지 않도록 최대값을 넣어준다. (0이하가 되면 점프 횟수가 줄어듦)
    }

#endregion

#region ResetJump

    private void ResetJump()
    {
        //상하 움직임이 없거나, 사다리를 타고 있을 때
        if (Mathf.Abs(_rigidbody2D.velocity.y) < 0.001f || climbLadder)
        {
            //코요테 타임 및 점프 카운트 초기화 
            jumpGraceTimer = JUMP_GRACE_TIME;
            jumpCount      = 0;
            _jumpDirection = _lastLadderJumpDirection = JumpDire.None;
            _player._animator.SetBool("IsJump", false);
        }
    }

#endregion

#region LadderAction

    [SerializeField] private Vector2 ladderPos;
    [SerializeField] private bool    onLadder;
    [SerializeField] private bool    climbLadder;

    [Serializable]
    public enum JumpDire
    {
        None,
        Up,
        Down,
        Left,
        Right
    }

    /// <summary>
    /// 플레이어의 사다리 관련 액션 처리
    /// </summary>
    [SerializeField] private float _previousLadderPos;

    private void ClimbLadder(Vector3 _pPosition)
    {
        if (!Controllable) return;

        //사다리를 타고있으면서, 상하 이동을 하지 않을때 velocity를 0으로
        if (climbLadder && input.vertical == 0)
        {
            _rigidbody2D.velocity   = Vector2.zero;
            _player._animator.speed = 0;
        }

        SetLadderStatus();

        //상하이동이 없거나 사다리를 타고있지 않으면 리턴
        if (input.vertical == 0 || !onLadder) return;

        //만약 _jumpDir이랑 내가 가고있는 방향이 같다면 사다리를 타지 않는다.
        //즉 내릴 마음으로 점프를 했다면 사다리를 타지 않는다.
        if (climbLadder && input.jumpDown)
            _lastLadderJumpDirection = GetNowJumpDirection();

        if (_jumpDirection                             == _lastLadderJumpDirection && //플레이어가 내리기 위해서 점프했음을 감지하여
            Math.Abs(_previousLadderPos - ladderPos.x) < .01f)
            return; //사다리에 다시 붙지 못하도록 한다.

        _player._animator.SetBool("IsClimb", true);

        climbLadder           = true;
        transform.position    = new(ladderPos.x, _pPosition.y); //사다리에 붙여주고
        _rigidbody2D.velocity = Vector2.zero;                   //가속 초기화

        //사다리에서 나갈때 틩기는 현상을 막기위해 velocity사용을 하지 않게 변경
        // if (climbLadder) //타고있는중에는 상하 이동만 해준다.
        //     _rigidbody2D.velocity = new Vector2(_rigidbody2D.velocity.x, Input.GetAxis("Vertical") * Speed);

        if (climbLadder)
        {
            if (input.vertical > 0                                           && //사다리에서 위로 올라가려고 할때
                HasTile(floorTilemap, transform.position + Vector3.up).Item1 && //위에 타일이 있는데
                !HasTile(ladderTilemap, transform.position + Vector3.up).Item1) //사다리는 없으면 못올라가게 한다.
                return;                                                         //(위가 막혀있는 사다리)

            float _deltaY = input.vertical * Speed * Time.deltaTime;
            transform.position += new Vector3(0, _deltaY, 0);
        }

        _player._animator.speed = 1;
    }

    /// <summary>
    /// 플레이어의 점프 방향 계산
    /// </summary>
    private JumpDire GetNowJumpDirection()
    {
        return input switch
        {
            { horizontal: > 0 }              => JumpDire.Right,
            { horizontal: < 0 }              => JumpDire.Left,
            { horizontal: 0, vertical: > 0 } => JumpDire.Up,
            { horizontal: 0, vertical: < 0 } => JumpDire.Down,
            _                                => JumpDire.None
        };
    }

    /// <summary>
    /// 사다리 탑승을 위한 플레이어 상태를 설정한다.
    /// </summary>
    private void SetLadderStatus()
    {
        var _hasTile = HasTile(ladderTilemap, transform.position);

        //현재 서있는 곳이 사다리인지 확인한다.
        if (_hasTile.Item1)
        {
            ladderPos = _hasTile.Item2;
            onLadder  = true;
            return;
        }

        if (!onLadder) return;
        onLadder                  = false;
        climbLadder               = false;
        _rigidbody2D.gravityScale = 1;
        _boxCollider2D.isTrigger  = false;
        _player._animator.SetBool("IsClimb", false);
    }

    /// <summary>
    /// 플레이어 위치에 해당 타일이 있는지 확인한다.
    /// </summary>
    private (bool, Vector2) HasTile(Tilemap _tilemap, Vector3 _targetPosition)
    {
        //플레이어의 위치를 타일맵의 로컬 좌표로 변환한다.
        Vector3 _localPosition = _tilemap.transform.InverseTransformPoint(_targetPosition);

        int _isUpInput = input.vertical > 0 ?
            1 :
            0;

        Vector3Int _tilePosition = new(Mathf.FloorToInt(_localPosition.x), //여기서 사다리 위에서 위키로 사다리에 타지 못하게 막는다.
                                       Mathf.FloorToInt(_localPosition.y + _isUpInput));

        //플레이어의 위치는 서있는 타일기준 2칸 위 이므로, 아래를 누를때는 하향 사다리가 존재하는 서있는 타일 위를 조사한다.
        //점프하고 사다리를 타면 공중에서 사다리를 타는 문제가 있으므로 y이동이 없을때만 사용한다.

        Debug.DrawRay(_tilemap.transform.TransformPoint(_tilePosition + new Vector3(.5f, .5f)), Vector3.right * 0.1f, Color.blue);
        if (!_tilemap.HasTile(_tilePosition)) return (false, Vector2.zero);

        //tilePosition은 HasTile을 사용하기위해서 Int로 변환했기 때문에 좌하단 좌표를 가리키고 있다.
        //따라서 타일의 중심을 가리키기 위해서는 0.5만큼 더해줘야한다.
        Vector3 _localLadderPos = new Vector2(_tilePosition.x + .5f, _tilePosition.y);

        //로컬 좌표를 다시 월드 좌표로 변환한다.
        Vector2 _tileWorldPosition = _tilemap.transform.TransformPoint(_localLadderPos);
        return (true, _tileWorldPosition);
    }

#endregion

#region Gravity

    [SerializeField] private float maxFallSpeed = 20;

    private void ClampVelocity()
    {
        //하강속도 제한
        if (_rigidbody2D.velocity.y < -maxFallSpeed)
            _rigidbody2D.velocity = new(_rigidbody2D.velocity.x, -maxFallSpeed);
    }

#endregion

#region SkillUse

    private bool IsSkillButtonPressed(SkillTypes _skillType)
    {
        return _skillType switch
        {
            SkillTypes.Primary   => input.primarySkill,
            SkillTypes.Secondary => input.secondarySkill,
            SkillTypes.Utility   => input.utilitySkill,
            SkillTypes.Ultimate  => input.ultimateSkill,
            SkillTypes.Passive   => input.specialSkill,
            SkillTypes.Item      => input.itemSkill,
            _                    => false
        };
    }

    //TODO: 임시 변수
    private bool _hasItem = false;

    private void UseSkill()
    {
        if (input.itemSkill && _hasItem) //아이템 스킬은 언제나 사용가능
            ((IUseable)_player.skills[SkillTypes.Item]).Play();

        if (climbLadder) return;

        //이외는 사다리에서 사용 불가능

        foreach (SkillTypes _skillTypes in Tools.GetEnumValues<SkillTypes>())
        {
            if (!IsSkillButtonPressed(_skillTypes) || !_player.skills[_skillTypes].IsReady) continue;
            if (_player.skills[_skillTypes] is IUseable _usableSkill)
                _usableSkill.Play();
            break;
        }
    }

#endregion

    public void SetControllable(bool _pPControllable)
    {
        if (_player.dead) //이미 죽었다면 다른 효과에 의한 조작을 무시한다.
        {
            Controllable = false;
            return;
        }

        //플레이어의 조작 가능 여부를 설정한다.
        Controllable = _pPControllable;
        if (!_pPControllable && _rigidbody2D.velocity.y == 0) //공중이 아니라면 x속도도 0으로 만들어준다.
            _rigidbody2D.velocity = new(0, _rigidbody2D.velocity.y);
    }
}