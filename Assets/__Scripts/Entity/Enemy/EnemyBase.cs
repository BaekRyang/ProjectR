using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MoreMountains.Feedbacks;
using UnityEngine;

public class EnemyBase : MonoBehaviour, IEntity
{
    private const float VERTICAL_OFFSET    = .7f;
    private const float CRITICAL_FONT_SIZE = 1.05f;

    private EnemyAI          _enemyAI;
    private MMF_Player       _damageFeedback;
    private MMF_FloatingText _floatingText;

    public EnemyStats stats;
    
    public event EventHandler OnEnemyHpChange;
    

    private void Start()
    {
        var _enemyData = DataManager.GetEnemyData("Frost");
        stats = new EnemyStats(_enemyData);

        if (_enemyData.isBoss)
            BossHealthDisplay.Instance.SyncToBossHealthBar(this);

        _enemyAI = GetComponent<EnemyAI>();
        _enemyAI.Initialize(this);
        _damageFeedback = transform.Find("DamageFeedback").GetComponent<MMF_Player>();
        _damageFeedback.Initialization();

        _floatingText = _damageFeedback.GetFeedbackOfType<MMF_FloatingText>();

        DifficultyManager.OnDifficultyChange += LevelUp;
    }

    private readonly Dictionary<uint, uint> _attackID = new();

    public void Attacked(int p_pDamage, bool p_isCritical, float p_stunDuration, Player p_pAttacker, uint? p_attackID = null)
    {
        _floatingText.Value = p_pDamage.ConvertDamageUnit(p_isCritical, Tools.DamageUnitType.Full);

        //크리티컬은 빨강 아니면 하양
        _floatingText.Intensity = p_isCritical ? CRITICAL_FONT_SIZE : 1;

        if (!p_attackID.HasValue) //ID가 없으면 기본 위치에
            _floatingText.TargetPosition = transform.position;
        else //ID가 있으면
        {
            if (_attackID.ContainsKey(p_attackID.Value)) //해당 ID의 공격이 이미 있으면
                _attackID[p_attackID.Value]++;           //해당 아이디의 value를 증가
            else                                         //없으면
                _attackID.Add(p_attackID.Value, 0);      //해당 ID의 공격을 만들고 value를 초기화


            _floatingText.TargetPosition = transform.position + Vector3.up * (VERTICAL_OFFSET * _attackID[p_attackID.Value]);
        }

        //여기서 오류나면 Exception 처리만 해주면 됨
        _damageFeedback.PlayFeedbacks();

        if (p_stunDuration == 0)
            _enemyAI.Daze();
        else
            _enemyAI.Stun(p_stunDuration);

        GetDamage(p_pDamage);

        if (stats.canKnockback)
            Knockback(p_pAttacker, 200);

        if (stats.Health <= 0) 
            Dead();
    }

    private async void Dead()
    {
        _enemyAI.animator.SetBool("IsDead", true);
        Destroy(_enemyAI);
        GetComponent<Rigidbody2D>().simulated = false;
        GetComponent<Collider2D>().enabled    = false;

        if (OnEnemyHpChange?.GetInvocationList().Length > 0) //구독중이면
            BossHealthDisplay.Instance.UnSyncToBossHealthBar(this);
        
        //현재 재생중인 애니메이션이 종료될때 까지 대기
        await new WaitUntil(() => _enemyAI.animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1);
        Destroy(this);
    }

    private void GetDamage(int _damage)
    {
        stats.Health -= _damage;

        //TODO: BossHealthBarDisplay에 어떤식으로 연결시켜서 값을 동기화 시킬까
        OnEnemyHpChange?.Invoke(this, EventArgs.Empty);
        
    }

    private void Knockback(Player p_pAttacker, float p_pKnockbackForce)
    {
        Vector2 _knockbackDirection = (transform.position - p_pAttacker.PlayerPosition).normalized;

        // GetComponent<Rigidbody2D>().velocity = _knockbackDirection * p_pKnockbackForce;
        GetComponent<Rigidbody2D>().AddForce(_knockbackDirection * p_pKnockbackForce, ForceMode2D.Impulse);
    }
    
    private void LevelUp(object p_sender, EventArgs p_args)
    {
    }
}

public class EEnemyHpChange
{
    public event EventHandler OnEnemyHpChange;

    public void HpChanged()
    {
        OnEnemyHpChange?.Invoke(this, EventArgs.Empty);
    }
}