using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;
using System.Collections.Generic;

// ============================================================================
// MAIN RUNTIME SCRIPT
// ============================================================================
public class TestCourse : MonoBehaviour
{
    [Header("Lookup names")]
    public string pathHolderName = "Path Holder";
    public string floorName = "floor";
    public string startName = "Path (4)";
    public string endName = "Path (1)";
    public string timingStartLineName = "Starting_line";
    public string timingEndLineName = "Ending_line";
    public string groundName = "Path";


    [Header("Ground Scale")]
    public Vector3 groundScale = new Vector3(100f, 1f, 500f);

    [Header("Start Transform")]
    public Vector3 startPosition = new Vector3(0, 0.5f, 15);
    public Vector3 startRotation = new Vector3(0f, 0f, 0f);
    public Vector3 startScale    = new Vector3(100f, 3f, 10f);

    [Header("End Transform")]
    public Vector3 endPosition = new Vector3(0, 0.5f, 130);
    public Vector3 endRotation = new Vector3(0f, 0f, 0f);
    public Vector3 endScale    = new Vector3(100f, 3f, 10f);

    [Header("Gap Controller")]
    public Vector3 gc_position = new Vector3(177f, 2.25f, 72f);

    [Header("Gap Generation")]
    public int NB_GAPS = 7;
    public float corridorWidth = 200f;
    public float minSquareGapSize = 7f;
    public float maxSquareGapSize = 50f;
    public float gapSizeStep = 5f;

    [Header("Gap Positioning")]
    public float firstGapZOffset = 0f;
    public float gapSpacing = 50f;
    public bool useFixedTotalGapCenterDistance = false;
    public float targetTotalGapCenterDistance = 300f;
    public float startLineGapOffset = 10f;
    public float finishLineGapOffset = 10f;

    public float[] initialGapCenters = new float[]
    {
        -10f, 30f, 0f, 20f, -30f, 10f, -20f
    };

    [Header("Random Gap Centers")]
    public bool randomizeGapCenters = false;
    [Tooltip("Inclusive random X range for each gap center, in GapController local space.")]
    public Vector2 randomGapCenterXRange = new Vector2(-30f, 30f);
    [Tooltip("Inclusive random Y range for each gap center. For 3D square gaps, keep the minimum at least wallHeight / 2.")]
    public Vector2 randomGapCenterYRange = new Vector2(25f, 50f);

    [Header("3D Square Gap Layout")]
    public bool generate3DGaps = true;
    [Tooltip("Set to 0 to randomize each square hole side length using the GapsController min/max range.")]
    public float squareGapSize = 0f;
    public Vector3 starEulerRotation = new Vector3(0f, -90f, 0f);
    public float[] initialGapCenterHeights = new float[]
    {
        25f, 35f, 45f, 30f, 40f, 50f, 32.5f
    };

    [Header("Wall Layout")]
    [Tooltip("In 3D square-gap mode this is the outer side length of each square wall panel.")]
    public float wallHeight = 50f;
    public float wallThickness = 5f;
    public float wallY = 25f;

    [Header("Prefab Auto-Load")]
    public string wallPrefabName = "ObstacleWall";
    public string wallPrefabFolder = "Assets/Prefab/DronePrefabUtilities";

    private GameObject wallPrefab;

    [HideInInspector]
    public Transform startLine;
    [HideInInspector]
    public Transform endLine;
    [HideInInspector]
    public Transform timingStartLine;
    [HideInInspector]
    public Transform timingEndLine;
    [HideInInspector]
    public Transform groundTile;

    [System.Serializable]
    public class ObstacleWall
    {
        public string name;
        public float x;
        public float y;
        public float z;
        public float width;
        public float height;
        public float length;
    }

    [System.Serializable]
    public class GapExport
    {
        public int index;
        public ObstacleWall left;
        public ObstacleWall right;
        public ObstacleWall top;
        public ObstacleWall bottom;
    }

    [System.Serializable]
    public class ObstacleCourseExport
    {
        public float courseWidth;
        public float courseLength;
        public ObstacleWall[] boundaryWalls;
        public GapExport[] gaps;
    }

    // -------------------------------
    // AUTO-FIND
    // -------------------------------
    public void AutoFindReferences()
    {
        Transform parent = transform.parent;

        Transform pathHolder =
            (parent != null ? parent.Find(pathHolderName) : null);

        if (pathHolder == null)
        {
            Debug.LogError("[TestCourse] Path Holder not found.");
            return;
        }

        Transform floor = pathHolder.Find(floorName);
        if (floor == null)
        {
            Debug.LogError("[TestCourse] floor not found.");
            return;
        }

        startLine = floor.Find(startName);
        endLine   = floor.Find(endName);
        timingStartLine = FindDeepChild(pathHolder, timingStartLineName);
        timingEndLine = FindDeepChild(pathHolder, timingEndLineName);
        if (timingStartLine == null)
        {
            GameObject foundStart = GameObject.Find(timingStartLineName);
            if (foundStart != null)
                timingStartLine = foundStart.transform;
        }
        if (timingEndLine == null)
        {
            GameObject foundEnd = GameObject.Find(timingEndLineName);
            if (foundEnd != null)
                timingEndLine = foundEnd.transform;
        }
        groundTile = floor.Find(groundName);

        if (startLine  == null) Debug.LogError("[TestCourse] Start not found");
        if (endLine    == null) Debug.LogError("[TestCourse] End not found");
        if (timingStartLine == null) Debug.LogError("[TestCourse] Timing start line not found: " + timingStartLineName);
        if (timingEndLine == null) Debug.LogError("[TestCourse] Timing end line not found: " + timingEndLineName);
        if (groundTile == null) Debug.LogError("[TestCourse] Ground not found");
    }

    private Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        Transform direct = root.Find(childName);
        if (direct != null)
            return direct;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
    }

    public Transform TrajectoryStartLine()
    {
        return timingStartLine != null ? timingStartLine : startLine;
    }

    public Transform TrajectoryEndLine()
    {
        return timingEndLine != null ? timingEndLine : endLine;
    }

#if UNITY_EDITOR
    // -------------------------------
    // LOAD PREFAB FROM PROJECT FOLDER
    // -------------------------------
    private GameObject LoadPrefab(string name, string folder)
    {
        string[] guids = AssetDatabase.FindAssets(name + " t:Prefab", new[] { folder });

        if (guids.Length == 0)
        {
            Debug.LogError("[TestCourse] Prefab not found in folder: " + folder + " Name = " + name);
            return null;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }
#endif

    public void Generate()
    {
        Clean();
        PlaceStartEnd();
        GenerateGaps();
    }

    public void Save()
    {
        Transform gcRoot = transform.Find("GapController");
        if (gcRoot == null)
        {
            Debug.LogWarning("[TestCourse] Cannot save: generate the obstacle course first.");
            return;
        }

        var controller = gcRoot.GetComponent<GapsController>();
        if (controller == null)
        {
            Debug.LogWarning("[TestCourse] Cannot save: GapsController component missing on GapController.");
            return;
        }

        AutoFindReferences();
        controller.Apply();

        var gaps = new List<Gap>(gcRoot.GetComponentsInChildren<Gap>(includeInactive: true));
        if (gaps.Count == 0)
        {
            Debug.LogWarning("[TestCourse] Cannot save: no gaps found.");
            return;
        }

        float courseWidth = controller.corridorWidth;
        float courseLength = Mathf.Max(0f, (gaps.Count - 1) * controller.gapSpacing + controller.gapWidth);

        var boundaryWalls = new List<ObstacleWall>();
        var leftBoundary = gcRoot.Find("BoundaryLeft");
        var rightBoundary = gcRoot.Find("BoundaryRight");
        if (leftBoundary != null) boundaryWalls.Add(ToWall(leftBoundary, "BoundaryLeft"));
        if (rightBoundary != null) boundaryWalls.Add(ToWall(rightBoundary, "BoundaryRight"));

        gaps.Sort((a, b) => a.transform.localPosition.z.CompareTo(b.transform.localPosition.z));
        var gapExports = new List<GapExport>();
        for (int i = 0; i < gaps.Count; i++)
        {
            Gap g = gaps[i];
            var left = g.leftWall != null ? ToWall(g.leftWall, "LeftWall") : null;
            var right = g.rightWall != null ? ToWall(g.rightWall, "RightWall") : null;
            var top = g.topWall != null && g.topWall.gameObject.activeSelf ? ToWall(g.topWall, "TopWall") : null;
            var bottom = g.bottomWall != null && g.bottomWall.gameObject.activeSelf ? ToWall(g.bottomWall, "BottomWall") : null;
            gapExports.Add(new GapExport
            {
                index = i,
                left = left,
                right = right,
                top = top,
                bottom = bottom
            });
        }

        var export = new ObstacleCourseExport
        {
            courseWidth = courseWidth,
            courseLength = courseLength,
            boundaryWalls = boundaryWalls.ToArray(),
            gaps = gapExports.ToArray()
        };

        string json = JsonUtility.ToJson(export, true);
        string dir = Path.Combine(Application.dataPath, "Data/default/ObstacleCourse");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "TestCourse.json");
        File.WriteAllText(path, json);
        Debug.Log("[TestCourse] Saved obstacle course to " + path);
    }

    private ObstacleWall ToWall(Transform t, string defaultName)
    {
        Vector3 p = t.position;
        Vector3 s = t.lossyScale;
        return new ObstacleWall
        {
            name = string.IsNullOrEmpty(t.name) ? defaultName : t.name,
            x = p.x,
            y = p.y,
            z = p.z,
            width = s.x,
            height = s.y,
            length = s.z
        };
    }

    public void Clean()
    {
        // 1. Delete ALL children of TestCourse
        #if UNITY_EDITOR
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        #else
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
        #endif

        // 2. Delete all FLOOR children except start, end, ground
        AutoFindReferences();

        if (startLine == null || endLine == null || groundTile == null)
            return;

        Transform floor = startLine.parent;

        #if UNITY_EDITOR
        for (int i = floor.childCount - 1; i >= 0; i--)
        {
            Transform c = floor.GetChild(i);
            if (c != startLine && c != endLine && c != groundTile)
                DestroyImmediate(c.gameObject);
        }
        #else
        for (int i = floor.childCount - 1; i >= 0; i--)
        {
            Transform c = floor.GetChild(i);
            if (c != startLine && c != endLine && c != groundTile)
                Destroy(c.gameObject);
        }
        #endif
    }

    // -------------------------------
    // BUTTON FUNCTION 1 — Place Start / End
    // -------------------------------
    public void PlaceStartEnd()
    {
        AutoFindReferences();
        if (startLine == null || endLine == null) return;

        startLine.position = startPosition;
        startLine.rotation = Quaternion.Euler(startRotation);
        startLine.localScale = startScale;

        endLine.position = endPosition;
        endLine.rotation = Quaternion.Euler(endRotation);
        endLine.localScale = endScale;

        // Basic scale for Y only
        groundTile.localScale = groundScale;

        // Align ground tile X position with the GapController
        Vector3 gp = groundTile.localPosition;
        gp.x = gc_position.x;
        gp.z = gc_position.z + firstGapZOffset + (NB_GAPS - 1) * gapSpacing / 2f;
        groundTile.localPosition = gp;

        Debug.Log("[TestCourse] Start & End fully positioned (pos+rot+scale).");
    }

    public float FirstGapWorldZ()
    {
        return gc_position.z + firstGapZOffset;
    }

    public float LastGapWorldZ(float spacing)
    {
        return FirstGapWorldZ() + Mathf.Max(0, NB_GAPS - 1) * spacing;
    }

    public void AlignTimingLinesToGeneratedGaps(GapsController controller)
    {
        AutoFindReferences();

        Transform trajectoryStartLine = TrajectoryStartLine();
        Transform trajectoryEndLine = TrajectoryEndLine();
        if (trajectoryStartLine == null && trajectoryEndLine == null)
            return;

        float firstGapZ = FirstGapWorldZ();
        float lastGapZ = LastGapWorldZ(gapSpacing);
        float lineX = transform.TransformPoint(gc_position).x;

        if (controller != null)
        {
            Gap[] gaps = controller.GetComponentsInChildren<Gap>(includeInactive: true);
            if (gaps.Length > 0)
            {
                Transform firstGap = gaps[0].transform;
                Transform lastGap = gaps[0].transform;
                for (int i = 1; i < gaps.Length; i++)
                {
                    if (gaps[i].transform.position.z < firstGap.position.z)
                        firstGap = gaps[i].transform;
                    if (gaps[i].transform.position.z > lastGap.position.z)
                        lastGap = gaps[i].transform;
                }

                firstGapZ = firstGap.position.z;
                lastGapZ = lastGap.position.z;
                lineX = firstGap.position.x;
            }
        }

        if (trajectoryStartLine != null)
        {
            Vector3 p = trajectoryStartLine.position;
            p.x = lineX;
            p.z = firstGapZ - startLineGapOffset;
            trajectoryStartLine.position = p;
        }

        if (trajectoryEndLine != null)
        {
            Vector3 p = trajectoryEndLine.position;
            p.x = lineX;
            p.z = lastGapZ + finishLineGapOffset;
            trajectoryEndLine.position = p;
        }
    }

    public void GenerateGaps()
    {
        AutoFindReferences();

#if UNITY_EDITOR
        wallPrefab = LoadPrefab(wallPrefabName, wallPrefabFolder);
        if (wallPrefab == null)
        {
            Debug.LogError("[TestCourse] Cannot generate gaps because wall prefab failed to load.");
            return;
        }
#endif

        GameObject gc = new GameObject("GapController");
        gc.transform.SetParent(this.transform, worldPositionStays: false);

        // Clean identity transform
        gc.transform.localPosition = gc_position;
        gc.transform.localRotation = Quaternion.identity;
        gc.transform.localScale = Vector3.one;

        var controller = gc.AddComponent<GapsController>();
        controller.corridorWidth = corridorWidth;
        controller.minGapSize = minSquareGapSize;
        controller.maxGapSize = maxSquareGapSize;
        controller.gapResolution = gapSizeStep;
        controller.gapSpacing = gapSpacing;
        controller.gapWidth = wallThickness;

        // Create gaps RELATIVE TO gc_position
        for (int i = 0; i < NB_GAPS; i++)
            CreateGap(gc.transform, i);

        controller.Apply();

        if (useFixedTotalGapCenterDistance)
        {
            AdjustGapCentersForTargetTotalDistance(controller, targetTotalGapCenterDistance);
            controller.Apply();
        }

        AlignTimingLinesToGeneratedGaps(controller);
        LogGapDistanceSummary(controller);
        Debug.Log("[TestCourse] Gaps generated and layout applied.");
    }

    private void AdjustGapCentersForTargetTotalDistance(GapsController controller, float targetTotalDistance)
    {
        Gap[] gaps = controller.GetComponentsInChildren<Gap>(includeInactive: true);
        if (gaps.Length < 2)
            return;

        System.Array.Sort(gaps, (a, b) => a.transform.localPosition.z.CompareTo(b.transform.localPosition.z));

        float fixedZSpacing = gapSpacing;
        float minTotalDistance = TotalGapCenterDistanceForSpacing(gaps, fixedZSpacing, 0f);
        float estimatedMaxTotalDistance = EstimateMaxTotalGapCenterDistance(gaps.Length, fixedZSpacing);
        if (targetTotalDistance <= minTotalDistance)
        {
            Debug.LogWarning(
                $"[TestCourse] Target gap center distance {targetTotalDistance:F2} is below the minimum possible " +
                $"{minTotalDistance:F2} with local gapSpacing={fixedZSpacing:F2}. Keeping generated centers.");
            return;
        }
        if (targetTotalDistance > estimatedMaxTotalDistance)
        {
            Debug.LogWarning(
                $"[TestCourse] Target gap center distance {targetTotalDistance:F2} is above the estimated maximum " +
                $"{estimatedMaxTotalDistance:F2} for the current XY ranges. " +
                "Increase randomGapCenterXRange/randomGapCenterYRange or lower the target.");
        }

        if (randomizeGapCenters)
        {
            RandomizeCentersUntilAtLeastTarget(gaps, fixedZSpacing, targetTotalDistance);
        }

        float currentTotalDistance = TotalGapCenterDistanceForSpacing(gaps, fixedZSpacing, 1f);
        if (currentTotalDistance < targetTotalDistance)
        {
            Debug.LogWarning(
                $"[TestCourse] Could only reach local gap center distance {currentTotalDistance:F2}, " +
                $"below target {targetTotalDistance:F2}. Increase randomGapCenterXRange/randomGapCenterYRange.");
            return;
        }

        Vector2[] originalCenters = GetGapCenters(gaps);
        Vector2 anchor = originalCenters[0];
        float low = 0f;
        float high = 1f;
        for (int i = 0; i < 40; i++)
        {
            float mid = (low + high) * 0.5f;
            if (TotalGapCenterDistanceForScaledCenters(originalCenters, anchor, fixedZSpacing, mid) < targetTotalDistance)
                low = mid;
            else
                high = mid;
        }

        ApplyScaledCenters(gaps, originalCenters, anchor, high);
    }

    private void RandomizeCentersUntilAtLeastTarget(Gap[] gaps, float fixedZSpacing, float targetTotalDistance)
    {
        const int maxAttempts = 200;
        float bestTotalDistance = TotalGapCenterDistanceForSpacing(gaps, fixedZSpacing, 1f);
        Vector2[] bestCenters = GetGapCenters(gaps);

        for (int attempt = 0; attempt < maxAttempts && bestTotalDistance < targetTotalDistance; attempt++)
        {
            for (int i = 0; i < gaps.Length; i++)
            {
                gaps[i].gapCenterX = RandomInRange(randomGapCenterXRange);
                gaps[i].gapCenterY = RandomInRange(GetSafeGapCenterYRange());
                ClampGapCenter(gaps[i]);
            }

            float totalDistance = TotalGapCenterDistanceForSpacing(gaps, fixedZSpacing, 1f);
            if (totalDistance > bestTotalDistance)
            {
                bestTotalDistance = totalDistance;
                bestCenters = GetGapCenters(gaps);
            }
        }

        ApplyCenters(gaps, bestCenters);
    }

    private float TotalGapCenterDistanceForSpacing(Gap[] gaps, float spacing, float xyScale)
    {
        float totalDistance = 0f;
        for (int i = 0; i < gaps.Length - 1; i++)
        {
            Vector2 currentCenter = new Vector2(gaps[i].gapCenterX, gaps[i].gapCenterY);
            Vector2 nextCenter = new Vector2(gaps[i + 1].gapCenterX, gaps[i + 1].gapCenterY);
            Vector2 centerDelta = (nextCenter - currentCenter) * xyScale;
            totalDistance += GapCenterDeltaMagnitude(centerDelta, spacing);
        }

        return totalDistance;
    }

    private float TotalGapCenterDistanceForScaledCenters(Vector2[] originalCenters, Vector2 anchor, float spacing, float scale)
    {
        float totalDistance = 0f;
        for (int i = 0; i < originalCenters.Length - 1; i++)
        {
            Vector2 currentCenter = anchor + ((originalCenters[i] - anchor) * scale);
            Vector2 nextCenter = anchor + ((originalCenters[i + 1] - anchor) * scale);
            Vector2 centerDelta = nextCenter - currentCenter;
            totalDistance += GapCenterDeltaMagnitude(centerDelta, spacing);
        }

        return totalDistance;
    }

    private float GapCenterDeltaMagnitude(Vector2 centerDelta, float spacing)
    {
        return new Vector3(centerDelta.x, centerDelta.y, spacing).magnitude;
    }

    private float EstimateMaxTotalGapCenterDistance(int gapCount, float spacing)
    {
        if (gapCount < 2)
            return 0f;

        Vector2 xRange = NormalizeRange(randomGapCenterXRange);
        Vector2 yRange = NormalizeRange(GetSafeGapCenterYRange());
        ClampEstimatedCenterRanges(ref xRange, ref yRange);
        Vector2 maxCenterDelta = new Vector2(xRange.y - xRange.x, yRange.y - yRange.x);
        return (gapCount - 1) * GapCenterDeltaMagnitude(maxCenterDelta, spacing);
    }

    private void ClampEstimatedCenterRanges(ref Vector2 xRange, ref Vector2 yRange)
    {
        float centerWidth = generate3DGaps ? Mathf.Max(0f, wallHeight) : maxSquareGapSize;
        float maxCenterX = (corridorWidth * 0.5f) - (centerWidth * 0.5f);
        xRange.x = Mathf.Clamp(xRange.x, -maxCenterX, maxCenterX);
        xRange.y = Mathf.Clamp(xRange.y, -maxCenterX, maxCenterX);

        if (generate3DGaps)
        {
            float minCenterY = Mathf.Max(0f, wallHeight) * 0.5f;
            yRange.x = Mathf.Max(yRange.x, minCenterY);
            yRange.y = Mathf.Max(yRange.y, yRange.x);
        }
    }

    private void ClampGapCenter(Gap gap)
    {
        if (gap == null)
            return;

        GapsController controller = gap.GetComponentInParent<GapsController>();
        if (controller == null)
            return;

        float halfCorridor = controller.corridorWidth * 0.5f;
        float wallSize = gap.useSquareHole
            ? Mathf.Max(0f, gap.squareWallSize)
            : (gap.leftWall != null ? Mathf.Max(0f, gap.leftWall.localScale.y) : 0f);
        if (wallSize <= 0f)
            return;

        float centerWidth = gap.useSquareHole ? wallSize : gap.gapSize;
        float maxCenterX = halfCorridor - (centerWidth * 0.5f);
        gap.gapCenterX = Mathf.Clamp(gap.gapCenterX, -maxCenterX, maxCenterX);

        float halfGapHeight = gap.gapHeight * 0.5f;
        if (gap.useSquareHole)
            gap.gapCenterY = Mathf.Max(gap.gapCenterY, wallSize * 0.5f);
        else
            gap.gapCenterY = Mathf.Clamp(gap.gapCenterY, halfGapHeight, wallSize - halfGapHeight);
    }

    private Vector2 NormalizeRange(Vector2 range)
    {
        return new Vector2(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }

    private Vector2[] GetGapCenters(Gap[] gaps)
    {
        Vector2[] centers = new Vector2[gaps.Length];
        for (int i = 0; i < gaps.Length; i++)
        {
            centers[i] = new Vector2(gaps[i].gapCenterX, gaps[i].gapCenterY);
        }
        return centers;
    }

    private void ApplyScaledCenters(Gap[] gaps, Vector2[] originalCenters, Vector2 anchor, float scale)
    {
        for (int i = 0; i < gaps.Length; i++)
        {
            Vector2 center = anchor + ((originalCenters[i] - anchor) * scale);
            gaps[i].gapCenterX = center.x;
            gaps[i].gapCenterY = center.y;
        }
    }

    private void ApplyCenters(Gap[] gaps, Vector2[] centers)
    {
        for (int i = 0; i < gaps.Length && i < centers.Length; i++)
        {
            gaps[i].gapCenterX = centers[i].x;
            gaps[i].gapCenterY = centers[i].y;
        }
    }

    private void LogGapDistanceSummary(GapsController controller)
    {
        if (controller == null)
            return;

        Gap[] gaps = controller.GetComponentsInChildren<Gap>(includeInactive: true);
        if (gaps.Length < 2)
        {
            Debug.Log("[TestCourse] Gap distance summary: fewer than 2 gaps.");
            return;
        }

        System.Array.Sort(gaps, (a, b) => a.transform.position.z.CompareTo(b.transform.position.z));

        float totalDistance = 0f;
        List<string> segments = new List<string>();
        for (int i = 0; i < gaps.Length - 1; i++)
        {
            Vector3 currentCenter = GapCenterWorldPosition(gaps[i]);
            Vector3 nextCenter = GapCenterWorldPosition(gaps[i + 1]);
            float distance = Vector3.Distance(currentCenter, nextCenter);
            totalDistance += distance;
            segments.Add($"Gap {i + 1}->{i + 2}: {distance:F2}");
        }

        Debug.Log($"[TestCourse] Gap distance summary: total={totalDistance:F2}; {string.Join(", ", segments)}");
    }

    private Vector3 GapCenterWorldPosition(Gap gap)
    {
        if (gap == null)
            return Vector3.zero;

        return gap.transform.TransformPoint(new Vector3(gap.gapCenterX, gap.gapCenterY, 0f));
    }

    private void CreateGap(Transform parent, int index)
    {
        GameObject gapGO = new GameObject("Gap (" + index + ")");
        gapGO.transform.parent = parent;

        Gap gap = gapGO.AddComponent<Gap>();
        gap.useSquareHole = generate3DGaps;
        if (generate3DGaps)
        {
            gap.squareWallSize = wallHeight;
            if (squareGapSize > 0f)
            {
                gap.gapSize = squareGapSize;
                gap.gapHeight = squareGapSize;
            }
            gap.starEulerRotation = starEulerRotation;
        }

        if (randomizeGapCenters)
        {
            gap.gapCenterX = RandomInRange(randomGapCenterXRange);
            gap.gapCenterY = RandomInRange(GetSafeGapCenterYRange());
        }
        else
        {
            // Assign initial centers if lists have enough values.
            if (initialGapCenters != null && index < initialGapCenters.Length)
            {
                gap.gapCenterX = initialGapCenters[index];
            }
            if (initialGapCenterHeights != null && index < initialGapCenterHeights.Length)
            {
                gap.gapCenterY = initialGapCenterHeights[index];
            }
        }

        // Pre controller gap positioning
        float localZ = firstGapZOffset + index * 0.1f;

        // Place gap relative to the controller
        gapGO.transform.localPosition = new Vector3(
            0f,
            0f,
            localZ
        );
        gapGO.transform.localScale = Vector3.one;

        // Create side walls
        Transform L = Instantiate(wallPrefab, gapGO.transform).transform;
        L.name = "LeftWall";
        gap.leftWall = L;

        Transform R = Instantiate(wallPrefab, gapGO.transform).transform;
        R.name = "RightWall";
        gap.rightWall = R;

        Transform T = Instantiate(wallPrefab, gapGO.transform).transform;
        T.name = "TopWall";
        gap.topWall = T;

        Transform B = Instantiate(wallPrefab, gapGO.transform).transform;
        B.name = "BottomWall";
        gap.bottomWall = B;

        // Side walls positioning
        Vector3 ls = L.localScale;
        ls.y = wallHeight;
        ls.x = wallThickness;
        L.localScale = ls;

        Vector3 rs = R.localScale;
        rs.y = wallHeight;
        rs.x = wallThickness;
        R.localScale = rs;

        Vector3 ts = T.localScale;
        ts.y = wallHeight;
        ts.x = wallThickness;
        T.localScale = ts;

        Vector3 bs = B.localScale;
        bs.y = wallHeight;
        bs.x = wallThickness;
        B.localScale = bs;

        Vector3 lp = L.localPosition;
        lp.y = wallY;
        L.localPosition = lp;

        Vector3 rp = R.localPosition;
        rp.y = wallY;
        R.localPosition = rp;

        Vector3 tp = T.localPosition;
        tp.y = wallY;
        T.localPosition = tp;

        Vector3 bp = B.localPosition;
        bp.y = wallY;
        B.localPosition = bp;

        gap.Initialize();
    }

    private float RandomInRange(Vector2 range)
    {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        return Random.Range(min, max);
    }

    private Vector2 GetSafeGapCenterYRange()
    {
        Vector2 range = randomGapCenterYRange;
        if (generate3DGaps)
        {
            float minCenterY = wallHeight * 0.5f;
            range.x = Mathf.Max(range.x, minCenterY);
            range.y = Mathf.Max(range.y, range.x);
        }
        return range;
    }

}

// ============================================================================
// Inspector Buttons
// ============================================================================
#if UNITY_EDITOR

[CustomEditor(typeof(TestCourse))]
public class TestCourseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TestCourse script = (TestCourse)target;

        GUILayout.Space(10);
        EditorGUILayout.LabelField("TestCourse Tools", EditorStyles.boldLabel);

        if (GUILayout.Button("Generate"))
            script.Generate();

        if (GUILayout.Button("Save Course"))
            script.Save();
    }
}
#endif
