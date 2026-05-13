using UnityEngine;

[ExecuteAlways]
public class TextureTilingByWorldBuiltIn : MonoBehaviour
{
    public Renderer target;
    public float metersPerTile = 1f;
    public bool autoMetersPerTileFromTexture = true;
    public float pixelsPerMeter = 500f;
    public bool useMaterialTiling = true;
    public bool useMaterialOffset = true;
    public Vector2 tilingMultiplier = Vector2.one;
    public Vector2 offset = Vector2.zero;
    public bool useLocalMeshSize = true;

    Vector3 _lastSize;
    float _lastMetersPerTile;
    bool _lastAutoMetersPerTile;
    float _lastPixelsPerMeter;
    float _lastEffectiveMetersPerTile;
    bool _lastUseMaterialTiling;
    bool _lastUseMaterialOffset;
    Vector2 _lastTilingMultiplier;
    Vector2 _lastOffset;
    Vector2 _lastBaseTiling;
    Vector2 _lastBaseOffset;
    MaterialPropertyBlock _mpb;

    void OnEnable()
    {
        Apply(true);
    }

    void OnValidate()
    {
        Apply(true);
    }

    void LateUpdate()
    {
        Apply(false);
    }

    void Apply(bool force)
    {
        if (!target)
            target = GetComponent<Renderer>();

        if (!target || metersPerTile <= 0f)
            return;

        Vector3 size = GetSizeForTiling();
        Material mat = target.sharedMaterial;
        Vector2 baseTiling = Vector2.one;
        Vector2 baseOffset = Vector2.zero;
        float effectiveMetersPerTile = metersPerTile;
        if (mat != null)
        {
            if (useMaterialTiling)
                baseTiling = mat.mainTextureScale;
            if (useMaterialOffset)
                baseOffset = mat.mainTextureOffset;
            if (autoMetersPerTileFromTexture && pixelsPerMeter > 0f && mat.mainTexture != null)
            {
                float texWidth = mat.mainTexture.width;
                if (texWidth > 0f)
                    effectiveMetersPerTile = texWidth / pixelsPerMeter;
            }
        }

        if (!force &&
            (size - _lastSize).sqrMagnitude < 0.0001f &&
            Mathf.Approximately(_lastMetersPerTile, metersPerTile) &&
            _lastAutoMetersPerTile == autoMetersPerTileFromTexture &&
            Mathf.Approximately(_lastPixelsPerMeter, pixelsPerMeter) &&
            Mathf.Approximately(_lastEffectiveMetersPerTile, effectiveMetersPerTile) &&
            _lastUseMaterialTiling == useMaterialTiling &&
            _lastUseMaterialOffset == useMaterialOffset &&
            _lastTilingMultiplier == tilingMultiplier &&
            _lastOffset == offset &&
            _lastBaseTiling == baseTiling &&
            _lastBaseOffset == baseOffset)
            return;

        _lastSize = size;
        _lastMetersPerTile = metersPerTile;
        _lastAutoMetersPerTile = autoMetersPerTileFromTexture;
        _lastPixelsPerMeter = pixelsPerMeter;
        _lastEffectiveMetersPerTile = effectiveMetersPerTile;
        _lastUseMaterialTiling = useMaterialTiling;
        _lastUseMaterialOffset = useMaterialOffset;
        _lastTilingMultiplier = tilingMultiplier;
        _lastOffset = offset;
        _lastBaseTiling = baseTiling;
        _lastBaseOffset = baseOffset;

        if (effectiveMetersPerTile <= 0f)
            return;

        Vector2 tiling = new Vector2(size.x / effectiveMetersPerTile, size.z / effectiveMetersPerTile);
        tiling = Vector2.Scale(tiling, tilingMultiplier);
        tiling = Vector2.Scale(tiling, baseTiling);
        Vector2 finalOffset = baseOffset + offset;

        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        target.GetPropertyBlock(_mpb);
        _mpb.SetVector("_MainTex_ST", new Vector4(tiling.x, tiling.y, finalOffset.x, finalOffset.y));
        target.SetPropertyBlock(_mpb);
    }

    Vector3 GetSizeForTiling()
    {
        if (useLocalMeshSize)
        {
            MeshFilter mf = target.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Vector3 localSize = mf.sharedMesh.bounds.size;
                Vector3 scale = target.transform.lossyScale;
                return new Vector3(
                    Mathf.Abs(localSize.x * scale.x),
                    Mathf.Abs(localSize.y * scale.y),
                    Mathf.Abs(localSize.z * scale.z));
            }
        }
        return target.bounds.size;
    }
}
