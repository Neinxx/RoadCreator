
using UnityEngine;

/// <summary>
/// 道路关键点信息
/// </summary>
[System.Serializable]
public struct RoadControlPoint
{
    public Vector3 position;
    // Tangente für zukünftige Erweiterungen (z.B. Bézier-Kurven).
    // Bei Catmull-Rom wird diese nicht direkt für die Kurvenform verwendet.
    public Vector3 tangent; 
    [Tooltip("Straßenneigung an diesem Punkt in Grad.")]
    [Range(-45, 45)]
    public float rollAngle; // Querneigungswinkel in Grad
}