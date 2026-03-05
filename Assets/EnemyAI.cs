using System.Collections;
using UnityEngine;
using UnityEngine.AI;
public class EnemyAI : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public NavMeshAgent agent;

    [Header("Patrol")]
    public Transform[] patrolPoints;
    private int currentPatrolIndex = 0;
    public float patrolSpeed = 1.5f;

    [Header("Detection")]
    public float sightRange = 15f;
    public float sightAngle = 70f; 
    public float hearingRange = 10f;
    public LayerMask obstacleMask;
    public LayerMask playerMask;

    [Header("Chase")]
    public float chaseSpeed = 3.5f;
    public float attackRange = 2.0f;
    public float attackCooldown = 1.5f;

    [Header("Search")]
    public float searchDuration = 10f;

    private Vector3 lastKnownPlayerPos;
    private bool playerInSight = false;
    private bool playerInHearingRange = false;
    private bool isChasing = false;
    private bool isSearching = false;
    private float attackTimer = 0f;
    private float searchTimer = 0f;

    private Animator animator;

    void Start()
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        if (player == null) Debug.LogError("Player not assigned in Enemy AI");

        agent.speed = patrolSpeed;
        agent.autoBraking = false;

        if (patrolPoints.Length == 0)
            Debug.LogWarning("No patrol points assigned");

        animator = GetComponent<Animator>();
        GoToNextPatrolPoint();
    }

    void Update()
    {
        if (player == null) return;

        DetectPlayer();

        if (playerInSight || playerInHearingRange)
        {
            lastKnownPlayerPos = player.position;
            isChasing = true;
            isSearching = false;
            searchTimer = 0f;
            ChasePlayer();
        }
        else if (isChasing)
        {
            // Lost sight, start searching
            if (!isSearching)
            {
                isSearching = true;
                searchTimer = 0f;
                agent.SetDestination(lastKnownPlayerPos);
            }

            SearchForPlayer();
        }
        else
        {
            Patrol();
        }

        attackTimer -= Time.deltaTime;
    }

    void DetectPlayer()
    {
        playerInSight = false;
        playerInHearingRange = false;

        Vector3 directionToPlayer = player.position - transform.position;
        float distanceToPlayer = directionToPlayer.magnitude;

        // Check sight
        if (distanceToPlayer <= sightRange)
        {
            float angle = Vector3.Angle(transform.forward, directionToPlayer.normalized);
            if (angle <= sightAngle)
            {
                // Check line of sight
                if (!Physics.Raycast(transform.position + Vector3.up * 1.5f, directionToPlayer.normalized, distanceToPlayer, obstacleMask))
                {
                    playerInSight = true;
                }
            }
        }

        // Check hearing (simple: distance only)
        if (distanceToPlayer <= hearingRange)
        {
            // noise logic will go here but we can do that later
            playerInHearingRange = true;
        }
    }

    void Patrol()
    {
        if (patrolPoints.Length == 0) return;

        agent.speed = patrolSpeed;

        if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            GoToNextPatrolPoint();
        }

        animator?.SetBool("isWalking", true);
        animator?.SetBool("isRunning", false);
    }

    void GoToNextPatrolPoint()
    {
        if (patrolPoints.Length == 0) return;

        agent.destination = patrolPoints[currentPatrolIndex].position;
        currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
    }

    void ChasePlayer()
    {
        agent.speed = chaseSpeed;
        agent.SetDestination(player.position);

        animator?.SetBool("isRunning", true);
        animator?.SetBool("isWalking", false);

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance <= attackRange)
        {
            Attack();
        }
    }

    void SearchForPlayer()
    {
        searchTimer += Time.deltaTime;

        animator?.SetBool("isRunning", false);
        animator?.SetBool("isWalking", true);

        if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            // Reached last known position, wait a bit
            if (searchTimer >= searchDuration)
            {
                // Give up, return to patrol
                isChasing = false;
                isSearching = false;
                GoToNextPatrolPoint();
            }
        }
    }

    void Attack()
    {
        if (attackTimer > 0f) return;

        attackTimer = attackCooldown;

        // TODO: Add attack animation and damage logic here
        Debug.Log($"{name} attacks the player!");

        animator?.SetTrigger("attack");
    }

    // visualize detection ranges in editor
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, hearingRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, sightRange);

        Vector3 leftBoundary = Quaternion.Euler(0, -sightAngle, 0) * transform.forward * sightRange;
        Vector3 rightBoundary = Quaternion.Euler(0, sightAngle, 0) * transform.forward * sightRange;

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(transform.position + Vector3.up * 1.5f, transform.position + Vector3.up * 1.5f + leftBoundary);
        Gizmos.DrawLine(transform.position + Vector3.up * 1.5f, transform.position + Vector3.up * 1.5f + rightBoundary);
    }
}
