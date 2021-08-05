using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

using Rito.ObjectPooling;

// 날짜 : 2021-08-03 PM 10:06:49
// 작성자 : Rito

namespace Rito.Tests
{
    using KeyType = System.String;
    /// <summary> 
    /// 
    /// </summary>
    public class Test_ObjectPool : MonoBehaviour
    {
        public KeyType key;
        public int number = 5;

        private List<GameObject> list = new List<GameObject>(100);

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.A))
            {
                for (int i = 0; i < number; i++)
                {
                    var go = ObjectPoolManager.I.Spawn(key);

                    if(list.Contains(go) == false)
                        list.Add(go);
                }
            }
            if (Input.GetKeyDown(KeyCode.D))
            {
                for (int i = 0; i < number; i++)
                {
                    if (list.Count > 0)
                    {
                        ObjectPoolManager.I.Despawn(list[0]);
                        list.RemoveAt(0);
                    }
                }
            }
            if (Input.GetKeyDown(KeyCode.G) && list.Count > 0)
            {
                int current = 0;
                const int Max = 5;

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].activeSelf)
                    {
                        ObjectPoolManager.I.Despawn(list[i], UnityEngine.Random.Range(1f, 3f));
                        current++;
                    }

                    if (current >= Max) break;
                }
            }
        }
    }
}