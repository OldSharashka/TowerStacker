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

        /// <summary>把指定面转到正对相机。</summary>
        public void FocusFace(FaceId face)
        {
            FaceFrame frame = FaceFrames.Get(face);
            Vector3 normal = ToVector(frame.Normal);
            Vector3 up = ToVector(frame.Up);

            // 让该面法线朝向相机(-Z)、该面的"上"朝世界 +Y
            Quaternion faceToWorld = Quaternion.LookRotation(-normal, up);
            _targetRotation = Quaternion.Inverse(faceToWorld);

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
