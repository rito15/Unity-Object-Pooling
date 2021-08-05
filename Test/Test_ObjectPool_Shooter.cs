using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;

using Rito.ObjectPooling;

// 날짜 : 2021-08-05 PM 9:24:15
// 작성자 : Rito

namespace Rito.Tests
{
    /// <summary> 
    /// 
    /// </summary>
    public class Test_ObjectPool_Shooter : MonoBehaviour
    {
        public GameObject _leftObjectPrefab;
        public GameObject _rightObjectPrefab;
        public string _leftObjectName;
        public string _rightObjectName;

        public float _shootInterval = 0.1f;
        private float _currentDurationLeft = 0f;
        private float _currentDurationRight = 0f;

        public float _distFromCamera = 1f;
        public float _lifeSpan = 2f;
        public float _speed = 5f;

        private void Update()
        {
            // Register : A
            if (Input.GetKeyDown(KeyCode.A))
            {
                if (_leftObjectPrefab != null)
                {
                    ObjectPoolManager.I.Register(_leftObjectName, _leftObjectPrefab);
                }
                if (_rightObjectName != null)
                {
                    ObjectPoolManager.I.Register(_rightObjectName, _rightObjectPrefab);
                }
            }

            // 관계 없는 녀석 Despawn 시도 : S
            if (Input.GetKeyDown(KeyCode.S))
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                ObjectPoolManager.I.Despawn(go);
            }

            // ReLoad Scene : R
            if (Input.GetKeyDown(KeyCode.R))
            {
                SceneManager.LoadScene(0);
            }

            if (Input.GetMouseButton(0))
            {
                Shoot(_leftObjectName, ref _currentDurationLeft);
            }
            if (Input.GetMouseButton(1))
            {
                Shoot(_rightObjectName, ref _currentDurationRight);
            }

            if (_currentDurationLeft > 0f)
                _currentDurationLeft -= Time.deltaTime;
            if (_currentDurationRight > 0f)
                _currentDurationRight -= Time.deltaTime;
        }

        private void Shoot(string name, ref float duration)
        {
            if (duration > 0f) return;

            GameObject go = ObjectPoolManager.I.Spawn(name);
            if (go == null) return;

            ObjectPoolManager.I.Despawn(go, _lifeSpan);
            SetPositionToMousePos(go);
            StartCoroutine(ShootRoutine(go));

            duration = _shootInterval;
        }

        private void SetPositionToMousePos(GameObject go)
        {
            Vector3 mPos = Input.mousePosition;
            mPos.z = _distFromCamera;

            go.transform.position = Camera.main.ScreenToWorldPoint(mPos);
        }

        private IEnumerator ShootRoutine(GameObject obj)
        {
            float t = 0f;
            Transform tr = obj.transform;
            Vector3 dir = (tr.position - Camera.main.transform.position).normalized;
            float myLifeSpan = _lifeSpan;

            while (t < myLifeSpan)
            {
                if (tr == null) yield break;
                tr.Translate(dir * _speed, Space.World);

                // 0.5% 확률로 수명 연장
                float r = UnityEngine.Random.Range(0f, 1f);
                if (r < 0.005f)
                {
                    t = 0f;
                    myLifeSpan = UnityEngine.Random.Range(1f, 2f);
                    myLifeSpan *= 10f;
                    myLifeSpan = Mathf.Floor(myLifeSpan) * 0.1f;
                    ObjectPoolManager.I.Despawn(obj, myLifeSpan);
                }

                t += Time.deltaTime;
                yield return null;
            }
        }
    }
}