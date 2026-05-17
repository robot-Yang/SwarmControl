using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Gap : MonoBehaviour
{
    [Header("Gap")]
    [Tooltip("X position of the center of the gap, in parent's local space.")]
    [Range(-30, 30)]
    public float gapCenterX;

    public float gapSize;
    [Tooltip("When enabled, the gap is a square hole in the wall: gapSize controls both width and height.")]
    public bool useSquareHole = true;
    [Tooltip("Outer side length of the square wall panel.")]
    public float squareWallSize = 50f;
    [Tooltip("Derived from gapSize for square-hole gaps.")]
    public float gapHeight = 15f;

    [Header("Walls (children of this object)")]
    public Transform leftWall;
    public Transform rightWall;
    public Transform topWall;
    public Transform bottomWall;

    // Star collectible config structure
    [System.Serializable]
    public struct StarConfig
    {
        public float offsetX;
        public float offsetY;
        public string starName;
        [HideInInspector] public Transform instance;
    }

    [Header("Stars")]
    public List<StarConfig> stars = new List<StarConfig>();
    public float gapCenterY = 18.3f;
    public Vector3 starEulerRotation = new Vector3(0f, -90f, 0f);
    [Tooltip("Rotation applied to this gap.transform around its own center. With gap.transform parked at the wall center, this rotates the whole gap+walls+stars in place — center position and inter-gap distances are preserved. TestCourse.GenerateGaps fills this in per-gap; leave at zero for unrotated gaps.")]
    public Vector3 gapEulerRotation = Vector3.zero;
    private const string collectiblePrefabName = "Star";
    private const string collectibleFolder     = "Assets/Prefab/";

    private float GetGapResolution()
    {
        GapsController controller = GetComponentInParent<GapsController>();
        if (controller == null || controller.gapResolution <= 0f)
            return 0f;
        return controller.gapResolution;
    }

    private int GetColumnCount(float resolution)
    {
        float effectiveGapSize = Mathf.Max(resolution, gapSize);
        return Mathf.Max(1, Mathf.RoundToInt(effectiveGapSize / resolution));
    }

    private int GetRowCount(float resolution)
    {
        float height = useSquareHole ? gapHeight : (leftWall != null ? leftWall.localScale.y : 0f);
        return Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(resolution, height) / resolution));
    }

    public void Initialize()
    {
    #if UNITY_EDITOR
        GameObject prefab = LoadPrefab(collectiblePrefabName, collectibleFolder);
        if (prefab == null) return;

        ResetStarsIfEmpty();

        string gapName = this.gameObject.name;

        // Instantiate all stars
        for (int i = 0; i < stars.Count; i++)
        {
            var sc = stars[i];
            if (sc.instance == null)
            {
                GameObject obj = PrefabUtility.InstantiatePrefab(prefab, this.transform) as GameObject;
                if (obj != null)
                {
                    string finalName = "Star_" + gapName + "_" + i;
                    obj.name = finalName;
                    sc.instance = obj.transform;
                }
            }
            stars[i] = sc;
        }

        UpdateStars();
    #endif
    }

    private void ResetStarsIfEmpty()
    {
        float resolution = GetGapResolution();
        if (resolution <= 0f)
            return;

        if (leftWall == null || rightWall == null)
            return;

        int columns = GetColumnCount(resolution);
        int rows = GetRowCount(resolution);
        int expected = rows * columns;

        if (stars == null)
            stars = new List<StarConfig>();

        if (stars.Count > expected)
        {
        #if UNITY_EDITOR
            for (int i = expected; i < stars.Count; i++)
                if (stars[i].instance != null)
                    DestroyImmediate(stars[i].instance.gameObject);
        #else
            for (int i = expected; i < stars.Count; i++)
                if (stars[i].instance != null)
                    Destroy(stars[i].instance.gameObject);
        #endif

            stars.RemoveRange(expected, stars.Count - expected);
        }

        while (stars.Count < expected)
            stars.Add(new StarConfig());
    }

    private void UpdateStars()
    {
        float resolution = GetGapResolution();
        if (resolution <= 0f)
            return;

        if (leftWall == null || rightWall == null)
            return;

        ResetStarsIfEmpty();

        int columns = GetColumnCount(resolution);
        int rows = GetRowCount(resolution);
        if (rows * columns == 0)
            return;

        float effectiveGapSize = Mathf.Max(resolution, gapSize);
        float halfGap = effectiveGapSize * 0.5f;
        float effectiveGapHeight = useSquareHole
            ? Mathf.Max(resolution, gapHeight)
            : Mathf.Max(resolution, leftWall.localScale.y);
        // baseY is the bottom of the star grid in this transform's local frame
        // (origin == gap center). For useSquareHole the gap hole is centered on
        // the origin; for !useSquareHole the wall sits at leftWall.localPosition.y.
        float baseY = useSquareHole
            ? -effectiveGapHeight * 0.5f
            : leftWall.localPosition.y - (leftWall.localScale.y * 0.5f);

        float startX = -halfGap + (resolution * 0.5f);
        float startY = baseY + (resolution * 0.5f);

    #if UNITY_EDITOR
        GameObject prefab = LoadPrefab(collectiblePrefabName, collectibleFolder);
    #endif

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                int idx = row * columns + col;
                if (idx >= stars.Count)
                    continue;

                var sc = stars[idx];

                sc.offsetX = startX + col * resolution;
                sc.offsetY = startY + row * resolution;
                sc.starName = "Star_" + this.gameObject.name + "_" + row + "_" + col;

            #if UNITY_EDITOR
                if (sc.instance == null && prefab != null)
                {
                    GameObject obj = PrefabUtility.InstantiatePrefab(prefab, this.transform) as GameObject;
                    if (obj != null)
                        sc.instance = obj.transform;
                }
            #endif

                if (sc.instance == null)
                {
                    stars[idx] = sc;
                    continue;
                }

            #if UNITY_EDITOR
                sc.instance.name = sc.starName;
            #endif
                sc.instance.localPosition = new Vector3(
                    sc.offsetX,
                    sc.offsetY,
                    0f
                );
                sc.instance.localRotation = Quaternion.Euler(starEulerRotation);

                SimpleCollectibleScript collectible = sc.instance.GetComponent<SimpleCollectibleScript>();
                if (collectible != null)
                {
                    collectible.rotate = false;
                    collectible.preserveInitialRotation = true;
                }

                BoxCollider boxCollider = sc.instance.GetComponent<BoxCollider>();
                if (boxCollider != null)
                {
                    Vector3 size = boxCollider.size;
                    size.x = resolution/2;
                    size.y = resolution/2;
                    boxCollider.size = size;
                }

                stars[idx] = sc;
            }
        }
    }


#if UNITY_EDITOR
    // Loads prefab by name in a folder
    private GameObject LoadPrefab(string name, string folder)
    {
        string[] guids = AssetDatabase.FindAssets(name + " t:Prefab", new[] { folder });
        if (guids.Length == 0)
        {
            // Debug.LogError("[Gap] Collectible prefab not found: " + name);
            return null;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }
#endif

    public void Apply()
    {
        if (leftWall == null || rightWall == null)
            return;

        // Fetch corridorWidth from parent GapsController
        GapsController controller = GetComponentInParent<GapsController>();
        if (controller == null)
            return;

        float corridorWidth = controller.corridorWidth;
        float wallHeight = useSquareHole
            ? Mathf.Max(0f, squareWallSize)
            : Mathf.Max(0f, leftWall.localScale.y);

        if (corridorWidth <= 0f || wallHeight <= 0f)
            return;

        float halfCorridor = corridorWidth * 0.5f;

        float outerSide = wallHeight;
        float maxGapSize = useSquareHole ? Mathf.Min(corridorWidth, outerSide) : corridorWidth;
        gapSize = Mathf.Clamp(gapSize, 0f, maxGapSize);
        gapHeight = useSquareHole
            ? gapSize
            : Mathf.Clamp(gapHeight, controller.gapResolution, wallHeight);

        float centerWidth = useSquareHole ? outerSide : gapSize;
        float maxCenter = halfCorridor - centerWidth * 0.5f;
        gapCenterX = Mathf.Clamp(gapCenterX, -maxCenter, maxCenter);
        float halfGapHeight = gapHeight * 0.5f;
        if (useSquareHole)
            gapCenterY = Mathf.Max(gapCenterY, outerSide * 0.5f);
        else
            gapCenterY = Mathf.Clamp(gapCenterY, halfGapHeight, wallHeight - halfGapHeight);

        // Pivot this transform at the gap/wall center so any rotation applied
        // to gap.transform rotates around the center (preserving center-to-center
        // distance between gaps). Only sync for unrotated gaps — rotated gaps
        // sit on a separate corridor segment whose positions are computed in
        // TestCourse.PositionRotatedSegment.
        if (gapEulerRotation == Vector3.zero)
        {
            Vector3 pos = transform.localPosition;
            pos.x = gapCenterX;
            pos.y = gapCenterY;
            transform.localPosition = pos;
        }

        // Apply the gap rotation AFTER position is set. Because the transform
        // sits at the wall center, this rotates the gap+walls+stars in place
        // and leaves the center position (and therefore inter-gap distances)
        // unchanged.
        transform.localRotation = Quaternion.Euler(gapEulerRotation);

        float halfGap = gapSize * 0.5f;

        // Wall edges relative to this transform's origin (== gap center).
        float leftEdge, rightEdge;
        if (useSquareHole)
        {
            leftEdge = -outerSide * 0.5f;
            rightEdge = outerSide * 0.5f;
        }
        else
        {
            // Wall spans the corridor; in this transform's frame the corridor
            // is shifted by -gapCenterX (transform origin is at gapCenterX in
            // GapController's frame).
            leftEdge = -halfCorridor - gapCenterX;
            rightEdge = halfCorridor - gapCenterX;
        }

        float gapLeft = -halfGap;
        float gapRight = halfGap;

        // For !useSquareHole the wall sits at world Y == TestCourse.wallY; offset
        // by -gapCenterY because this transform is now at gapCenterY.
        float wallLocalY = 0f;
        if (!useSquareHole)
        {
            TestCourse tc = GetComponentInParent<TestCourse>();
            float wallY = tc != null ? tc.wallY : 0f;
            wallLocalY = wallY - gapCenterY;
        }

        // ---------- LEFT WALL ----------
        float leftWidth = Mathf.Max(0f, gapLeft - leftEdge);
        Vector3 ls = leftWall.localScale;
        ls.x = leftWidth;
        if (useSquareHole)
            ls.y = outerSide;
        leftWall.localScale = ls;

        Vector3 lp = leftWall.localPosition;
        lp.x = leftEdge + leftWidth * 0.5f;
        lp.y = wallLocalY;
        leftWall.localPosition = lp;

        // ---------- RIGHT WALL ----------
        float rightWidth = Mathf.Max(0f, rightEdge - gapRight);
        Vector3 rs = rightWall.localScale;
        rs.x = rightWidth;
        if (useSquareHole)
            rs.y = outerSide;
        rightWall.localScale = rs;

        Vector3 rp = rightWall.localPosition;
        rp.x = rightEdge - rightWidth * 0.5f;
        rp.y = wallLocalY;
        rightWall.localPosition = rp;

        if (topWall != null)
        {
            topWall.gameObject.SetActive(useSquareHole);
        }
        if (bottomWall != null)
        {
            bottomWall.gameObject.SetActive(useSquareHole);
        }

        if (useSquareHole && topWall != null && bottomWall != null)
        {
            float gapBottom = -halfGapHeight;
            float gapTop = halfGapHeight;
            float outerBottom = -outerSide * 0.5f;
            float outerTop = outerSide * 0.5f;
            float bottomHeight = Mathf.Max(0f, gapBottom - outerBottom);
            float topHeight = Mathf.Max(0f, outerTop - gapTop);

            Vector3 bs = bottomWall.localScale;
            bs.x = gapSize;
            bs.y = bottomHeight;
            bottomWall.localScale = bs;

            Vector3 bp = bottomWall.localPosition;
            bp.x = 0f;
            bp.y = outerBottom + bottomHeight * 0.5f;
            bottomWall.localPosition = bp;

            Vector3 ts = topWall.localScale;
            ts.x = gapSize;
            ts.y = topHeight;
            topWall.localScale = ts;

            Vector3 tp = topWall.localPosition;
            tp.x = 0f;
            tp.y = gapTop + topHeight * 0.5f;
            topWall.localPosition = tp;
        }

        UpdateStars();
    }

    private void OnValidate()
    {
        ResetStarsIfEmpty();
        Apply();
    }

}
