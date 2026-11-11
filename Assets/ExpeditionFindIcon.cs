using UnityEngine;

// UI uses exactly the pickup's paper drawing, not a differently styled copy.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class ExpeditionFindIcon : UnityEngine.UI.MaskableGraphic
{
    private ExpeditionFindKind kind;
    public ExpeditionFindKind Kind
    {
        get => kind;
        set { if (kind == value) return; kind = value; SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper helper)
    {
        helper.Clear();
        Mesh artwork = ExpeditionArt.FindMesh(kind);
        if (artwork == null) return;
        Vector3[] vertices = artwork.vertices; Color32[] colors = artwork.colors32; int[] indices = artwork.triangles;
        Rect area = rectTransform.rect; float scale = Mathf.Min(area.width, area.height) / .52f;
        for (int i = 0; i < vertices.Length; i++) helper.AddVert((Vector3)area.center + vertices[i] * scale,
            i < colors.Length ? colors[i] : (Color32)Color.white, Vector2.zero);
        for (int i = 0; i < indices.Length; i += 3) helper.AddTriangle(indices[i], indices[i + 1], indices[i + 2]);
    }
}
