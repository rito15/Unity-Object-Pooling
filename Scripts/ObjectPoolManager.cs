#if UNITY_EDITOR
#define DEBUG_ON
#define TEST_ON
#endif

using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

// 날짜 : 2021-07-26 AM 2:28:21
// 작성자 : Rito

namespace Rito.ObjectPooling
{
    using KeyType = System.String;

    /// <summary>  오브젝트 풀 관리 싱글톤 매니저 </summary>
    [DisallowMultipleComponent]
    public class ObjectPoolManager : MonoBehaviour
    {
        /***********************************************************************
        *                               Singleton
        ***********************************************************************/
        #region .
        /// <summary> 싱글톤 인스턴스 Getter </summary>
        public static ObjectPoolManager I
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<ObjectPoolManager>();
                    if (_instance == null) _instance = ContainerObject.GetComponent<ObjectPoolManager>();
                }
                return _instance;
            }
        }

        /// <summary> 싱글톤 인스턴스 Getter </summary>
        public static ObjectPoolManager Instance => I;
        private static ObjectPoolManager _instance;

        /// <summary> 싱글톤 게임오브젝트의 참조 </summary>
        private static GameObject ContainerObject
        {
            get
            {
                if (_containerObject == null)
                {
                    _containerObject = new GameObject($"[Singleton] {nameof(ObjectPoolManager)}");
                    if (_instance == null) _instance = ContainerObject.AddComponent<ObjectPoolManager>();
                }

                return _containerObject;
            }
        }
        private static GameObject _containerObject;

        /// <summary> true : 정상 작동, false : 자신 파괴 </summary>
        private bool CheckSingleton()
        {
            // 싱글톤 인스턴스가 미리 존재하지 않았을 경우, 본인으로 초기화
            if (_instance == null)
            {
                DebugLog($"싱글톤 생성 : {nameof(ObjectPoolManager)}, 게임 오브젝트 : {name}");

                _instance = this;
                _containerObject = gameObject;
            }

            // 싱글톤 인스턴스가 존재하는데, 본인이 아닐 경우, 스스로(컴포넌트)를 파괴
            if (_instance != null && _instance != this)
            {
                DebugLog($"이미 {nameof(ObjectPoolManager)} 싱글톤이 존재하므로 오브젝트를 파괴합니다.");

                var components = gameObject.GetComponents<Component>();
                if (components.Length <= 2) Destroy(gameObject);
                else Destroy(this);

                return false;
            }

            return true;
        }
        #endregion
        /***********************************************************************
        *                               Definitions
        ***********************************************************************/
        #region .
        /// <summary> 풀링 대상 오브젝트에 대한 정보 </summary>
        [System.Serializable]
        private class PoolObjectData
        {
            public const int INITIAL_COUNT = 10;
            public const int MAX_COUNT = 50;

            public KeyType key;
            public GameObject prefab;
            public int initialObjectCount = INITIAL_COUNT; // 오브젝트 초기 생성 개수
            public int maxObjectCount = MAX_COUNT;     // 큐 내에 보관할 수 있는 오브젝트 최대 개수
        }

        /// <summary> 복제된 오브젝트의 예약 정보 </summary>
        private class CloneScheduleInfo
        {
            public readonly GameObject clone;
            public readonly Stack<GameObject> pool;
            public bool DespawnScheduled => _despawnScheduled;

            private bool _despawnScheduled;
            private int _scheduleVersion;

            public CloneScheduleInfo(GameObject clone, Stack<GameObject> pool)
            {
                this.clone = clone;
                this.pool = pool;
                this._despawnScheduled = false;
                this._scheduleVersion = 0;
            }

            /// <summary> Despawn 예약하고 버전 반환 </summary>
            public int ScheduleToDespawn()
            {
                _despawnScheduled = true;
                _scheduleVersion++;

                return _scheduleVersion;
            }

            /// <summary> 예약 취소 </summary>
            public void CancelSchedule()
            {
                if (_despawnScheduled == false) return;

                _despawnScheduled = false;
                _scheduleVersion++;
            }

            /// <summary> 예약 유효성 검증 </summary>
            public bool IsScheduleValid(int prevVersion)
            {
                return _despawnScheduled && (prevVersion == _scheduleVersion);
            }
        }

        /// <summary> 풀에서 제공되지 않은 오브젝트에 대한 Despawn 처리 </summary>
        private enum NonePoolObjectDespawnPolicy
        {
            Ignore,
            ThrowException,
            Destroy
        }

        #endregion
        /***********************************************************************
        *                               Inspector Fields
        ***********************************************************************/
        #region .
        [Header("Debug Options")]
        [SerializeField] private bool _debugOn = true;
        [SerializeField] private bool _debugRegister = true;
        [SerializeField] private bool _debugSpawn    = true;
        [SerializeField] private bool _debugDespawn  = true;

        [Space]
        [SerializeField]
        private bool _testModeOn = true;

        [Space]
        [SerializeField]
        private float _poolCleaningInterval = 0.1f; // 풀 한도 초과 오브젝트 제거 간격

        [SerializeField]
        private NonePoolObjectDespawnPolicy _nonePoolObjectDespawnPolicy = NonePoolObjectDespawnPolicy.Destroy;

        [Space]
        [SerializeField]
        private List<PoolObjectData> _poolObjectDataList = new List<PoolObjectData>(4);
        #endregion
        /***********************************************************************
        *                               Private Fields
        ***********************************************************************/
        #region .
        private Dictionary<KeyType, GameObject> _sampleDict;      // Key - 복제용 오브젝트 원본
        private Dictionary<KeyType, PoolObjectData> _dataDict;    // Key - 풀 정보
        private Dictionary<KeyType, Stack<GameObject>> _poolDict; // Key - 풀
        private Dictionary<GameObject, CloneScheduleInfo> _cloneDict;     // 복제된 게임오브젝트 - 클론 정보

        // 디스폰 스케줄링 동기화 큐
        private readonly ConcurrentQueue<CloneScheduleInfo> _despawnScheduleQueue = new ConcurrentQueue<CloneScheduleInfo>();

        #endregion
        /***********************************************************************
        *                               Unity Events
        ***********************************************************************/
        #region .
        private void Awake()
        {
            if (CheckSingleton() == false) return;

            Init();
        }
        private void Update()
        {
            HandleScheduledDespawning();
        }
        #endregion
        /***********************************************************************
        *                               Debug, Test
        ***********************************************************************/
        #region .
        private Dictionary<KeyType, GameObject> _t_ContainerDict;
        private Dictionary<Stack<GameObject>, KeyType> _t_poolKeyDict;

        [System.Diagnostics.Conditional("DEBUG_ON")]
        private void DebugLog(string msg)
        {
            if (!_debugOn) return;
            Debug.Log(msg);
        }

        [System.Diagnostics.Conditional("DEBUG_ON")]
        private void DebugLog(bool condition, string msg)
        {
            if (!_debugOn) return;
            if (condition) Debug.Log(msg);
        }

        [System.Diagnostics.Conditional("TEST_ON")]
        private void TestModeOnly(Action action)
        {
            if (!_testModeOn) return;
            action();
        }

        [System.Diagnostics.Conditional("TEST_ON")]
        private void Test_ChangeContainerName(KeyType key)
        {
            if (!_testModeOn) return;
            Stack<GameObject> pool = _poolDict[key];

            int cloneCount = _cloneDict.Values.Where(v => v.pool == pool).Count();
            int inPoolCount = pool.Count;
            int maxCount = _dataDict[key].maxObjectCount;

            _t_ContainerDict[key].name
                = $"Pool <{key}> - [{cloneCount - inPoolCount}] Used, [{inPoolCount}] Available, [{maxCount}] Max";
        }

        #endregion
        /***********************************************************************
        *                               Private Methods
        ***********************************************************************/
        #region .
        private void Init()
        {
            DebugLog("INIT");

            TestModeOnly(() =>
            {
                _t_ContainerDict = new Dictionary<KeyType, GameObject>();
                _t_poolKeyDict = new Dictionary<Stack<GameObject>, KeyType>();
            });

            int len = _poolObjectDataList.Count;
            if (len == 0) return;

            // 1. Dictionary 생성
            _sampleDict = new Dictionary<KeyType, GameObject>(len);
            _dataDict   = new Dictionary<KeyType, PoolObjectData>(len);
            _poolDict   = new Dictionary<KeyType, Stack<GameObject>>(len);
            _cloneDict  = new Dictionary<GameObject, CloneScheduleInfo>(len * PoolObjectData.INITIAL_COUNT);

            // 2. Data로부터 새로운 Pool 오브젝트 정보 생성
            foreach (var data in _poolObjectDataList)
            {
                RegisterInternal(data);
            }
        }

        /// <summary> 샘플 오브젝트 복제하기 </summary>
        private GameObject CloneFromSample(KeyType key)
        {
            if (!_sampleDict.TryGetValue(key, out GameObject sample)) return null;

            return Instantiate(sample);
        }

        /// <summary> 각 풀마다 한도 개수를 초과할 경우, 점진적으로 내부 오브젝트 파괴 </summary>
        private IEnumerator PoolCleanerRoutine(KeyType key)
        {
            if (!_poolDict.TryGetValue(key, out var pool)) yield break;
            if (!_dataDict.TryGetValue(key, out var data)) yield break;
            WaitForSeconds wfs = new WaitForSeconds(_poolCleaningInterval);

            while (true)
            {
                if (pool.Count > data.maxObjectCount)
                {
                    GameObject clone = pool.Pop(); // 풀에서 꺼내기
                    _cloneDict.Remove(clone);      // Clone - Pool 딕셔너리에서 제거
                    Destroy(clone);                // 게임오브젝트 파괴

                    Test_ChangeContainerName(key); // 컨테이너 이름 변경
                }

                yield return wfs;
            }
        }

        #endregion
        /***********************************************************************
        *                               Register
        ***********************************************************************/
        #region .
        /// <summary> Pool 데이터로부터 새로운 Pool 오브젝트 정보 등록 </summary>
        private void RegisterInternal(PoolObjectData data)
        {
            DebugLog($"Register : {data.key}");

            // 중복 키는 등록 불가능
            if (_poolDict.ContainsKey(data.key))
            {
                DebugLog(_debugRegister, $"{data.key}가 이미 Pool Queue Dict에 존재합니다.");
                return;
            }

            // 1. 샘플 게임오브젝트 생성, PoolObject 컴포넌트 존재 확인
            GameObject sample = Instantiate(data.prefab);
            sample.name = data.prefab.name;
            sample.SetActive(false);

            // 2. Pool Dictionary에 풀 생성 + 풀에 미리 오브젝트들 만들어 담아놓기
            Stack<GameObject> pool = new Stack<GameObject>(data.maxObjectCount);
            for (int i = 0; i < data.initialObjectCount; i++)
            {
                GameObject clone = Instantiate(data.prefab);
                clone.SetActive(false);
                pool.Push(clone);

                _cloneDict.Add(clone, new CloneScheduleInfo(clone, pool)); // Clone-Data 캐싱
            }

            // 3. 딕셔너리에 추가
            _sampleDict.Add(data.key, sample);
            _dataDict.Add(data.key, data);
            _poolDict.Add(data.key, pool);

            // 4. 클리너 코루틴 시작
            StartCoroutine(PoolCleanerRoutine(data.key));

            TestModeOnly(() =>
            {
                // 샘플을 공통 게임오브젝트의 자식으로 묶기
                string posName = "ObjectPool Samples";
                GameObject parentOfSamples = GameObject.Find(posName);
                if (parentOfSamples == null)
                    parentOfSamples = new GameObject(posName);

                sample.transform.SetParent(parentOfSamples.transform);

                // 풀 - 키 딕셔너리에 추가
                _t_poolKeyDict.Add(pool, data.key);

                // 컨테이너 게임오브젝트 생성
                _t_ContainerDict.Add(data.key, new GameObject());

                // 컨테이너 자식으로 설정
                foreach (var item in pool)
                {
                    item.transform.SetParent(_t_ContainerDict[data.key].transform);
                }

                // 컨테이너 이름 변경
                Test_ChangeContainerName(data.key);
            });
        }

        /// <summary> 키를 등록하고 새로운 풀 생성 </summary>
        public void Register(KeyType key, GameObject prefab, 
            int initalCount = PoolObjectData.INITIAL_COUNT, int maxCount = PoolObjectData.MAX_COUNT)
        {
            // 중복 키는 등록 불가능
            if (_poolDict.ContainsKey(key))
            {
                DebugLog(_debugRegister, $"{key}가 이미 Pool Queue Dict에 존재합니다.");
                return;
            }

            if (initalCount < 0) initalCount = 0;
            if (maxCount < 10) maxCount = 10;

            PoolObjectData data = new PoolObjectData
            {
                key = key,
                prefab = prefab,
                initialObjectCount = initalCount,
                maxObjectCount = maxCount
            };
            _poolObjectDataList.Add(data);

            RegisterInternal(data);
        }

        #endregion
        /***********************************************************************
        *                               Spawn
        ***********************************************************************/
        #region .
        /// <summary> 풀에서 꺼내오기 </summary>
        public GameObject Spawn(KeyType key)
        {
            // 키가 존재하지 않는 경우 null 리턴
            if (!_poolDict.TryGetValue(key, out var pool))
            {
                DebugLog(_debugSpawn, $"Fatal Error - Spawn() : [{key}] 키가 존재하지 않습니다.");
                return null;
            }

            GameObject go;

            // 1. 풀에 재고가 있는 경우 : 꺼내오기
            if (pool.Count > 0)
            {
                go = pool.Pop();
                DebugLog(_debugSpawn, $"Spawn : {go.name}({go.GetInstanceID()})");
            }
            // 2. 재고가 없는 경우 샘플로부터 복제
            else
            {
                go = CloneFromSample(key);
                _cloneDict.Add(go, new CloneScheduleInfo(go, pool)); // Clone-Data 캐싱
                DebugLog(_debugSpawn, $"Spawn[Create] : {go.name}({go.GetInstanceID()})");
            }

            go.SetActive(true);
            go.transform.SetParent(null); // 자식 해제

            TestModeOnly(() =>
            {
                // 컨테이너의 자식으로 추가
                go.transform.SetParent(_t_ContainerDict[key].transform);

                // 컨테이너 이름 변경
                Test_ChangeContainerName(key);
            });

            return go;
        }

        #endregion
        /***********************************************************************
        *                               Despawn
        ***********************************************************************/
        #region .
        /// <summary> Despawn 실제 처리 </summary>
        private void DespawnInternal(CloneScheduleInfo data)
        {
            // 예약되어 있던 경우, 해제
            data.CancelSchedule();

            // 풀에 집어넣기
            data.clone.SetActive(false);
            data.pool.Push(data.clone);

            TestModeOnly(() =>
            {
                KeyType key = _t_poolKeyDict[data.pool];

                // 컨테이너 자식으로 넣기
                data.clone.transform.SetParent(_t_ContainerDict[key].transform);

                // 컨테이너 이름 변경
                Test_ChangeContainerName(key);
            });
        }

        /// <summary> 풀에 집어넣기 </summary>
        public void Despawn(GameObject go)
        {
            if (go == null) return;
            if (go.activeSelf == false) return;

            // 복제된 게임오브젝트가 아닌 경우 - 정책에 따라 처리
            if (!_cloneDict.TryGetValue(go, out var cloneData))
            {
                switch (_nonePoolObjectDespawnPolicy)
                {
                    case NonePoolObjectDespawnPolicy.Ignore:
                        DebugLog(_debugDespawn, $"풀에서 제공된 오브젝트가 아닙니다 : {go.name}");
                        break;

                    case NonePoolObjectDespawnPolicy.ThrowException:
                        throw new ArgumentException($"풀에서 제공된 오브젝트가 아닙니다 : {go.name}");

                    case NonePoolObjectDespawnPolicy.Destroy:
                        DebugLog(_debugDespawn, $"풀에서 제공하지 않은 오브젝트를 파괴합니다 : {go.name}({go.GetInstanceID()})");
                        Destroy(go);
                        break;
                }

                return;
            }

            DespawnInternal(cloneData);

            DebugLog(_debugDespawn && cloneData.DespawnScheduled, $"Despawn 예약 해제 및 즉시 실행 : {go.name}({go.GetInstanceID()})");
            DebugLog(_debugDespawn && !cloneData.DespawnScheduled, $"Despawn : {go.name}({go.GetInstanceID()})");
        }

        /// <summary> n초 후 풀에 집어넣기 </summary>
        public void Despawn(GameObject go, float seconds)
        {
            if (go == null) return;
            if (go.activeSelf == false) return;

            int version;

            // 1. 풀에서 제공한 오브젝트가 아닌 경우 - 정책에 따라 처리
            if (_cloneDict.TryGetValue(go, out CloneScheduleInfo data) == false)
            {
                switch (_nonePoolObjectDespawnPolicy)
                {
                    case NonePoolObjectDespawnPolicy.Ignore:
                        DebugLog(_debugDespawn, $"풀에서 제공된 오브젝트가 아닙니다 : {go.name}");
                        break;

                    case NonePoolObjectDespawnPolicy.ThrowException:
                        throw new ArgumentException($"풀에서 제공된 오브젝트가 아닙니다 : {go.name}");

                    case NonePoolObjectDespawnPolicy.Destroy:
                        DebugLog(_debugDespawn, $"풀에서 제공하지 않은 오브젝트를 {seconds}초 후 파괴합니다 : {go.name}({go.GetInstanceID()})");
                        Destroy(go, seconds);
                        break;
                }

                return;
            }
            // 2. 풀에서 제공한 오브젝트가 맞는 경우
            else
            {
                // 0초 이하로 설정한 경우, 즉시 풀에 집어넣기
                if (seconds <= 0f)
                {
                    DespawnInternal(data);
                    return;
                }

                DebugLog(_debugDespawn && !data.DespawnScheduled, $"{seconds}초 후 Despawn 예약 : {go.name}({go.GetInstanceID()})");
                DebugLog(_debugDespawn && data.DespawnScheduled, $"{seconds}초 후 Despawn 예약[갱신] : {go.name}({go.GetInstanceID()})");

                // 정상 : 예약 설정
                version = data.ScheduleToDespawn();
            }

            // 예약
            Task.Run(async () => 
            {
                int prevVersion = version;

                await Task.Delay((int)(seconds * 1000));
                
                // 예약 정보가 유효한 경우, 큐에 넣기
                if (go != null && data.IsScheduleValid(prevVersion))
                {
                    _despawnScheduleQueue.Enqueue(data);
                }
            });
        }

        /// <summary> Despawn 예약된 오브젝트들 확인하여 처리 </summary>
        private void HandleScheduledDespawning()
        {
            if (_despawnScheduleQueue.Count == 0) return;

            while (_despawnScheduleQueue.TryDequeue(out CloneScheduleInfo data))
            {
                // 예약이 취소된 경우, 종료
                if (data.DespawnScheduled == false)
                    continue;
                // ------------------------------------------

                DespawnInternal(data);

#if DEBUG_ON
                if(_debugOn && data != null && data.clone != null)
                    DebugLog(_debugDespawn, $"예약된 Despawn 처리 : {data.clone.name}({data.clone.GetInstanceID()})");
#endif
            }
        }

        #endregion
    }
}