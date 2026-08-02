using UnityEngine;
using FishNet.Object;
using System.Collections.Generic;

public class Player_FOV : NetworkBehaviour
{
    [Header("FOV Settings")]
    public float innerRadius = 3f; // 360 degree close range vision
    public float viewRadius = 15f;
    [Range(0, 360)]
    public float viewAngle = 90f;
    public LayerMask obstacleMask;

    [Header("Fog of War Settings")]
    public float fogHeight = 0.05f; // Set to ground level to prevent perspective offset
    [Range(0.1f, 5f)]
    public float meshResolution = 1f; // Number of rays per degree
    public float outerRadius = 150f; // Distance of the fog boundary
    public float edgeFadeDistance = 2f; // Soft boundary blur distance
    public float edgeBlendAngle = 15f; // Soft angular transition at the edges of the fan
    public Color fogColor = new Color(0, 0, 0, 0.7f);

    private MeshFilter viewMeshFilter;
    private Mesh viewMesh;
    private GameObject fogObject;

    private Player player;
    private Enemy[] allEnemies;
    private Player[] allPlayers;
    private float enemyUpdateTimer;

    private void Awake()
    {
        player = GetComponent<Player>();
        
        // Setup Fog Mesh Object
        fogObject = new GameObject("FogOfWarMesh");
        fogObject.transform.SetParent(transform, false); // Local position 0
        fogObject.transform.localPosition = new Vector3(0, fogHeight, 0);
        
        // Make sure it ignores raycasts
        fogObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        
        viewMeshFilter = fogObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = fogObject.AddComponent<MeshRenderer>();
        
        // Use a simple transparent material
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = fogColor;
        meshRenderer.material = mat;

        viewMesh = new Mesh();
        viewMesh.name = "FogOfWar Mesh";
        viewMeshFilter.mesh = viewMesh;
    }
    
    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!base.IsOwner)
        {
            // If not owner, we don't need to render FOV or manage enemies
            fogObject.SetActive(false);
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (!base.IsOwner) return;
        
        // Use exact aim point from AimController as the center of the FOV
        Vector3 aimDirection = transform.forward;
        if (player.aim != null && player.aim.Aim() != null)
        {
            aimDirection = (player.aim.Aim().position - transform.position).normalized;
        }
        else if (player.playerBody != null)
        {
            aimDirection = player.playerBody.forward;
        }

        DrawFieldOfView(aimDirection);
        HandleEnemyVisibility(aimDirection);
    }

    private void HandleEnemyVisibility(Vector3 aimDirection)
    {
        // Don't search for enemies every frame to save performance, update the list periodically
        enemyUpdateTimer -= Time.deltaTime;
        if (enemyUpdateTimer <= 0)
        {
            allEnemies = FindObjectsOfType<Enemy>();
            allPlayers = FindObjectsOfType<Player>();
            enemyUpdateTimer = 0.5f; // Update enemy list every 0.5s
        }

        if (allEnemies == null) return;

        foreach (Enemy enemy in allEnemies)
        {
            if (enemy == null) continue;

            bool isVisible = false;
            
            // Calculate distance and direction
            Vector3 dirToTarget = (enemy.transform.position - transform.position).normalized;
            float dstToTarget = Vector3.Distance(transform.position, enemy.transform.position);

            if (dstToTarget < innerRadius)
            {
                // Close range 360 degree vision
                Vector3 startPos = transform.position + Vector3.up * 1f;
                Vector3 endPos = enemy.transform.position + Vector3.up * 1f;
                if (!Physics.Linecast(startPos, endPos, obstacleMask))
                {
                    isVisible = true;
                }
            }
            else if (dstToTarget < viewRadius)
            {
                // Angle check (only check Y axis) for fan-shaped vision
                Vector3 flatAim = new Vector3(aimDirection.x, 0, aimDirection.z).normalized;
                Vector3 flatDir = new Vector3(dirToTarget.x, 0, dirToTarget.z).normalized;
                
                if (Vector3.Angle(flatAim, flatDir) < viewAngle / 2f)
                {
                    // Obstacle check
                    // Raycast slightly above ground (e.g. at chest height)
                    Vector3 startPos = transform.position + Vector3.up * 1f;
                    Vector3 endPos = enemy.transform.position + Vector3.up * 1f;
                    
                    if (!Physics.Linecast(startPos, endPos, obstacleMask))
                    {
                        isVisible = true;
                    }
                }
            }

            // Instead of disabling the root object, we disable the renderers on visuals
            // This prevents the enemy logic and networking from breaking
            Renderer[] renderers = enemy.visuals != null ? enemy.visuals.GetComponentsInChildren<Renderer>(true) : enemy.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                // Optionally skip specific layers like UI
                if (r.gameObject.layer == LayerMask.NameToLayer("UI")) continue;
                r.enabled = isVisible;
            }

            UI_HealthBar healthBar = enemy.GetComponent<UI_HealthBar>();
            if (healthBar != null) healthBar.SetVisibleByFOV(isVisible);
        }

        if (allPlayers == null) return;

        foreach (Player otherPlayer in allPlayers)
        {
            if (otherPlayer == null || otherPlayer == this.player) continue;

            bool isVisible = false;
            
            // Calculate distance and direction
            Vector3 dirToTarget = (otherPlayer.transform.position - transform.position).normalized;
            float dstToTarget = Vector3.Distance(transform.position, otherPlayer.transform.position);

            if (dstToTarget < innerRadius)
            {
                // Close range 360 degree vision
                Vector3 startPos = transform.position + Vector3.up * 1f;
                Vector3 endPos = otherPlayer.transform.position + Vector3.up * 1f;
                if (!Physics.Linecast(startPos, endPos, obstacleMask))
                {
                    isVisible = true;
                }
            }
            else if (dstToTarget < viewRadius)
            {
                Vector3 flatAim = new Vector3(aimDirection.x, 0, aimDirection.z).normalized;
                Vector3 flatDir = new Vector3(dirToTarget.x, 0, dirToTarget.z).normalized;
                
                if (Vector3.Angle(flatAim, flatDir) < viewAngle / 2f)
                {
                    Vector3 startPos = transform.position + Vector3.up * 1f;
                    Vector3 endPos = otherPlayer.transform.position + Vector3.up * 1f;
                    
                    if (!Physics.Linecast(startPos, endPos, obstacleMask))
                    {
                        isVisible = true;
                    }
                }
            }

            Renderer[] renderers = otherPlayer.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (r.gameObject.layer == LayerMask.NameToLayer("UI")) continue;
                r.enabled = isVisible;
            }
            
            UI_HealthBar healthBar = otherPlayer.GetComponent<UI_HealthBar>();
            if (healthBar != null) healthBar.SetVisibleByFOV(isVisible);
        }
    }

    private void DrawFieldOfView(Vector3 aimDirection)
    {
        int stepCount = Mathf.RoundToInt(360 * meshResolution);
        float stepAngleSize = 360f / stepCount;

        List<Vector3> v0_list = new List<Vector3>();
        List<Vector3> v1_list = new List<Vector3>();
        List<Vector3> v2_list = new List<Vector3>();
        List<Vector3> v3_list = new List<Vector3>();
        List<float> t_list = new List<float>();

        // Current world angle of the aim direction
        Vector3 flatAim = new Vector3(aimDirection.x, 0, aimDirection.z).normalized;
        float aimAngle = Vector3.SignedAngle(Vector3.forward, flatAim, Vector3.up);
        if (aimAngle < 0) aimAngle += 360;

        for (int i = 0; i <= stepCount; i++)
        {
            float angle = stepAngleSize * i;
            
            // Check if this angle is within the FOV cone relative to aim direction
            float globalAngle = angle;
            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(globalAngle, aimAngle));
            
            float t = 0f;
            if (angleDiff < (viewAngle / 2f) - edgeBlendAngle || edgeBlendAngle <= 0f)
            {
                t = 0f;
            }
            else if (angleDiff < viewAngle / 2f)
            {
                float normalizedAngle = (angleDiff - ((viewAngle / 2f) - edgeBlendAngle)) / edgeBlendAngle; // 0 to 1
                t = Mathf.SmoothStep(0f, 1f, normalizedAngle);
            }
            else
            {
                t = 1f;
            }
            
            Vector3 dir = DirFromAngle(globalAngle, true);
            
            // Cast ray to find obstacle
            Vector3 rayOrigin = transform.position + Vector3.up * 1f;
            float hitDistance = viewRadius;
            
            if (Physics.Raycast(rayOrigin, dir, out RaycastHit hit, viewRadius, obstacleMask))
            {
                Vector3 flatHit = new Vector3(hit.point.x, 0, hit.point.z);
                Vector3 flatPos = new Vector3(transform.position.x, 0, transform.position.z);
                hitDistance = Vector3.Distance(flatPos, flatHit);
            }

            float r0 = Mathf.Min(innerRadius, hitDistance);
            float r1 = Mathf.Min(innerRadius + edgeFadeDistance, hitDistance);
            float r2 = Mathf.Min(viewRadius, hitDistance);
            float r3 = r2 + edgeFadeDistance;

            Vector3 localDir = fogObject.transform.InverseTransformDirection(dir);
            Vector3 centerPoint = fogObject.transform.InverseTransformPoint(transform.position);
            centerPoint.y = 0;

            v0_list.Add(centerPoint + localDir * r0);
            v1_list.Add(centerPoint + localDir * r1);
            v2_list.Add(centerPoint + localDir * r2);
            v3_list.Add(centerPoint + localDir * r3);
            
            t_list.Add(t);
        }

        // Build the inverted mesh (3 rings of quads)
        int vertexCount = (stepCount + 1) * 4;
        Vector3[] vertices = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];
        Color[] colors = new Color[vertexCount];
        int[] triangles = new int[stepCount * 18];

        for (int i = 0; i <= stepCount; i++)
        {
            vertices[i] = v0_list[i];
            vertices[i + stepCount + 1] = v1_list[i];
            vertices[i + (stepCount + 1) * 2] = v2_list[i];
            vertices[i + (stepCount + 1) * 3] = v3_list[i];
            
            uvs[i] = Vector2.zero;
            uvs[i + stepCount + 1] = Vector2.zero;
            uvs[i + (stepCount + 1) * 2] = Vector2.one;
            uvs[i + (stepCount + 1) * 3] = Vector2.one;
            
            float t = t_list[i];
            
            // Apply vertex colors for smooth true angular gradient
            colors[i] = new Color(1, 1, 1, 0);                 // Always invisible hole start
            colors[i + stepCount + 1] = new Color(1, 1, 1, t); // Soft inner ring fade
            colors[i + (stepCount + 1) * 2] = new Color(1, 1, 1, t); // Main fan body (darkens at edges)
            colors[i + (stepCount + 1) * 3] = new Color(1, 1, 1, 1); // Solid outer boundary

            if (i < stepCount)
            {
                int v0_idx = i;
                int v0_next = i + 1;
                
                int v1_idx = i + stepCount + 1;
                int v1_next = i + stepCount + 2;

                int v2_idx = i + (stepCount + 1) * 2;
                int v2_next = i + (stepCount + 1) * 2 + 1;

                int v3_idx = i + (stepCount + 1) * 3;
                int v3_next = i + (stepCount + 1) * 3 + 1;

                // Quad 1: v0 to v1 (Inner circle soft edge)
                triangles[i * 18] = v0_idx;
                triangles[i * 18 + 1] = v1_next;
                triangles[i * 18 + 2] = v0_next;

                triangles[i * 18 + 3] = v0_idx;
                triangles[i * 18 + 4] = v1_idx;
                triangles[i * 18 + 5] = v1_next;

                // Quad 2: v1 to v2 (Main Fan / Angular Transition)
                triangles[i * 18 + 6] = v1_idx;
                triangles[i * 18 + 7] = v2_next;
                triangles[i * 18 + 8] = v1_next;

                triangles[i * 18 + 9] = v1_idx;
                triangles[i * 18 + 10] = v2_idx;
                triangles[i * 18 + 11] = v2_next;

                // Quad 3: v2 to v3 (Outer radial soft edge)
                triangles[i * 18 + 12] = v2_idx;
                triangles[i * 18 + 13] = v3_next;
                triangles[i * 18 + 14] = v2_next;

                triangles[i * 18 + 15] = v2_idx;
                triangles[i * 18 + 16] = v3_idx;
                triangles[i * 18 + 17] = v3_next;
            }
        }

        viewMesh.Clear();
        viewMesh.vertices = vertices;
        viewMesh.uv = uvs;
        viewMesh.colors = colors;
        viewMesh.triangles = triangles;
        viewMesh.RecalculateNormals();
    }

    public Vector3 DirFromAngle(float angleInDegrees, bool angleIsGlobal)
    {
        if (!angleIsGlobal)
        {
            angleInDegrees += transform.eulerAngles.y;
        }
        return new Vector3(Mathf.Sin(angleInDegrees * Mathf.Deg2Rad), 0, Mathf.Cos(angleInDegrees * Mathf.Deg2Rad));
    }
}
