// Copyright 2022-2025 Niantic.

using UnityEngine;

namespace NianticSpatial.NSDK.Examples.Helpers
{
    public class LoadingAnimation : MonoBehaviour
    {
        [SerializeField]
        private float _rotationSpeed = 200f;

        void Update()
        {
            transform.Rotate(0f, 0f, -_rotationSpeed * Time.deltaTime);
        }
    }
}
