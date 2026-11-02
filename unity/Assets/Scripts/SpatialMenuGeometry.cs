using UnityEngine;
namespace StremioVR {
public static class SpatialMenuGeometry {
 // Independent surface planes preserve controller pointing on the angled navigation wing.
 public static bool Intersect(Transform surface,Ray ray,out float distance){return new Plane(surface.forward,surface.position).Raycast(ray,out distance)&&distance>0&&distance<4;}
}
}
