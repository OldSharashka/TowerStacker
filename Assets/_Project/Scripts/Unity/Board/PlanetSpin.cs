using Tower.Core;
using UnityEngine;

namespace Tower.Unity.View
{
    /// <summary>
    /// 球体朝向：手动拖动旋转 + 回合自动转场。相机固定不动，只转球。
    /// 两个动作共用一个 rotation，避免"相机和球各转一半"的错位。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlanetSpin : MonoBehaviour
    {
        [Tooltip("拖动灵敏度：每像素旋转多少度")]
        public float degreesPerPixel = 0.25f;

        [Tooltip("回合转场时长（秒）")]
        public float transitionDuration = 0.5f;

        private Quaternion _startRotation;
        private Quaternion _targetRotation;
        private float _elapsed;
        private bool _transitioning;

        public bool IsTransitioning => _transitioning;

        private void Awake()
        {
            _startRotation = transform.rotation;
            _targetRotation = transform.rotation;
        }

        /// <summary>
        /// 把指定面转到「正对相机」：该面法线指向相机，且该面的"上"对齐相机的上。
        /// 注意要按相机实际方向算，不能写死 -Z，否则相机不在轴上时看着是斜的。
        /// </summary>
        public void FocusFace(FaceId face, Camera cam)
        {
            FaceFrame frame = FaceFrames.Get(face);
            Vector3 normal = ToVector(frame.Normal);
            Vector3 faceUp = ToVector(frame.Up);

            Vector3 toCamera = cam != null
                ? (cam.transform.position - transform.position).normalized
                : Vector3.back;

            Vector3 upProj = cam != null
                ? Vector3.ProjectOnPlane(cam.transform.up, toCamera)
                : Vector3.up;

            if (upProj.sqrMagnitude < 1e-4f) upProj = Vector3.up;
            upProj.Normalize();

            // S = 目标朝向 × 该面本地朝向的逆：把 normal→toCamera、faceUp→upProj
            Quaternion faceToWorld = Quaternion.LookRotation(normal, faceUp);
            Quaternion target = Quaternion.LookRotation(toCamera, upProj) * Quaternion.Inverse(faceToWorld);

            _targetRotation = target;
            _startRotation = transform.rotation;
            _elapsed = 0f;
            _transitioning = true;
        }

        /// <summary>按屏幕位移手动旋转（只在未转场时生效）。</summary>
        public void RotateBy(float deltaX, float deltaY, Camera cam)
        {
            if (_transitioning) return;

            transform.Rotate(Vector3.up, -deltaX * degreesPerPixel, Space.World);
            if (cam != null)
            {
                transform.Rotate(cam.transform.right, deltaY * degreesPerPixel, Space.World);
            }
        }

        private void Update()
        {
            if (!_transitioning) return;

            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(transitionDuration <= 0f ? 1f : _elapsed / transitionDuration);
            float eased = 1f - (1f - t) * (1f - t);      // ease-out

            transform.rotation = Quaternion.Slerp(_startRotation, _targetRotation, eased);

            if (t >= 1f)
            {
                transform.rotation = _targetRotation;
                _transitioning = false;
            }
        }

        private static Vector3 ToVector(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
