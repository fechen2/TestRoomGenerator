using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;


/// <summary>
/// 一.简易版本的测试(一个Room即一个ceil)
///   1.类似教程生成ceilRoom(此ceils内包含特殊房间)
///   2.把终点Room从ceils中识别出来并且放到endrooms中去
///   3.从endRooms中获取boss房间
///   4.从endRooms中获取奖励房间和金币房间
///   5.隐藏房间暂时不做
///   ref:https://www.boristhebrave.com/2020/09/12/dungeon-generation-in-binding-of-isaac/
/// </summary>
public class TestRoomGenerator_Simple : MonoBehaviour
{
    private Vector2 cellSize = new Vector2(100, 80);
    [SerializeField] private SimpleRoomDataGenerator _roomDataGenerator = new();
    [SerializeField] private GameObject _startPrefab;
    [SerializeField] private GameObject _normalPrefab;
    [SerializeField] private GameObject _bossPrefab;
    [SerializeField] private GameObject _rewardPrefab;
    [SerializeField] private GameObject _coinPrefab;

    [SerializeField] private Transform _parentTrans;
    private List<GameObject> _normalRooms = new List<GameObject>();
    private GameObject _startRoom;
    private GameObject _bossRoom;
    private GameObject _rewardRoom;
    private GameObject _coinRoom;

    private void OnGUI()
    {
        if (GUI.Button(new Rect(0, 0, 300, 100), "Gen"))
        {
            DestroyRooms();
            bool success = _roomDataGenerator.GenerateRooms();

            if (success)
            {
                _startRoom = Instantiate(_startPrefab, _parentTrans);
                _startRoom.GetComponent<RectTransform>().anchoredPosition = GetRoomRectPos(_roomDataGenerator.startRoom);

                foreach (var room in _roomDataGenerator.normalRooms)
                {
                    GameObject go = Instantiate(_normalPrefab, _parentTrans);
                    go.GetComponent<RectTransform>().anchoredPosition = GetRoomRectPos(room);
                    _normalRooms.Add(go);
                }

                _bossRoom = Instantiate(_bossPrefab, _parentTrans);
                _bossRoom.GetComponent<RectTransform>().anchoredPosition = GetRoomRectPos(_roomDataGenerator.bossRoom);

                _rewardRoom = Instantiate(_rewardPrefab, _parentTrans);
                _rewardRoom.GetComponent<RectTransform>().anchoredPosition = GetRoomRectPos(_roomDataGenerator.rewardRoom);

                _coinRoom = Instantiate(_coinPrefab, _parentTrans);
                _coinRoom.GetComponent<RectTransform>().anchoredPosition = GetRoomRectPos(_roomDataGenerator.coinRoom);
            }
        }
    }

    private Vector2 GetRoomRectPos(Vector2Int roomPos)
    {
        Vector2Int startPos = _roomDataGenerator.startRoom;
        return new Vector2((roomPos.x - startPos.x) * cellSize.x, (roomPos.y - startPos.y) * cellSize.y);
    }

    private void DestroyRooms()
    {
        int childs = _parentTrans.childCount;
        for (int i = childs - 1; i > 0; i--)
        {
            GameObject.Destroy(transform.GetChild(i).gameObject);
        }
    }
}

[Serializable]
public class SimpleRoomDataGenerator
{
    [SerializeField] private int MaxHorizontalCount;
    [SerializeField] private int MaxVerticalCount;
    [SerializeField] private int MinRoomCount;
    [SerializeField] private int MaxRoomCount;
    [SerializeField] private int SafeCount = 1000;

    [SerializeField] private float _appropriatePercentV1 = 3.0f;
    [SerializeField] private float _appropriatePercentV2 = 7.0f;
    private float _appropriatePercent = 3.0f / 7.0f;
    [SerializeField] private float _errorMaxPercentV1 = 5f;
    [SerializeField] private float _errorMaxPercentV2 = 5f;
    private float _errorMaxPercent = 5f / 5f;
    [SerializeField] private float _errorMinPercentV1 = 5f;
    [SerializeField] private float _errorMinPercentV2 = 5f;
    private float _errorMinPercent = 5f / 5f;

    private int[,] _map;
    public Vector2Int startRoom { get; private set; }
    public Vector2Int bossRoom { get; private set; }
    public Vector2Int rewardRoom { get; private set; }
    public Vector2Int coinRoom { get; private set; }
    public List<Vector2Int> normalRooms { get; private set; } = new();
    private List<Vector2Int> _rooms = new();
    private List<Vector2Int> _endRooms = new();
    private List<Vector2Int> _linkRooms = new();

    /// <summary>
    ///this count contains boss room, reward room, gold room
    /// </summary>
    private int _currentRoomCount;

    private int _currentGenerateCount;

    public void Init()
    {
        _appropriatePercent = _appropriatePercentV1 / _appropriatePercentV2;
        _errorMaxPercent = _errorMaxPercentV1 / _errorMaxPercentV2;
        _errorMinPercent = _errorMinPercentV1 / _errorMinPercentV2;
        if (_map == null)
        {
            _map = new int[MaxHorizontalCount + 1, MaxVerticalCount + 1];
            startRoom = new Vector2Int(Mathf.FloorToInt((MaxHorizontalCount + 1) * 0.5f), Mathf.FloorToInt((MaxVerticalCount + 1) * 0.5f));
        }

        for (int i = 0; i < MaxHorizontalCount + 1; i++)
        {
            for (int j = 0; j < MaxVerticalCount + 1; j++)
            {
                _map[i, j] = 0;
            }
        }

        _rooms.Clear();
        _endRooms.Clear();
        _currentRoomCount = 0;
        bossRoom = Vector2Int.zero;
        rewardRoom = Vector2Int.zero;
        coinRoom = Vector2Int.zero;
        normalRooms.Clear();
        _linkRooms.Clear();

        //set start room to _rooms
        AddCoordToRoom(startRoom);
        //
    }

    public bool GenerateRooms()
    {
        _currentGenerateCount = 0;
        while (_currentGenerateCount < SafeCount)
        {
            _currentGenerateCount++;
            Init();
            if (GenerateRooms_Once())
            {
                return true;
            }
        }

        Debug.LogError($" _currentGenerateCount > {SafeCount} pls check algorithm");

        return false;
    }

    private bool GenerateRooms_Once()
    {
        while (_rooms.Count > 0)
        {
            Vector2Int currentRoom = _rooms[0];
            bool isCreated = false;
            if (currentRoom.x > 1 && CheckAndSetCeil(currentRoom + Vector2Int.left)) isCreated = true;
            if ((currentRoom.x < MaxHorizontalCount - 1) && CheckAndSetCeil(currentRoom + Vector2Int.right)) isCreated = true;
            if (currentRoom.y > 1 && CheckAndSetCeil(currentRoom + Vector2Int.down)) isCreated = true;
            if ((currentRoom.y < MaxVerticalCount - 1) && CheckAndSetCeil(currentRoom + Vector2Int.up)) isCreated = true;
            if (!isCreated)
            {
                _endRooms.Add(currentRoom);
            }
            else
            {
                _linkRooms.Add(currentRoom);
            }

            _rooms.RemoveAt(0);
        }

        if (_currentRoomCount < MinRoomCount || _endRooms.Count == 0) return false;

        bossRoom = _endRooms[^1];
        _endRooms.RemoveAt(_endRooms.Count - 1);
        (bool rCoin, Vector2Int posCoin) = GetRandomEndRoom();
        (bool rReward, Vector2Int posReward) = GetRandomEndRoom();

        if (rCoin && rReward)
        {
            #region 房间个数百分比判断

            //1.相对x轴倾斜45度和135度，判断区域房间个数比例
            //2.x轴y轴判断区域房间个数比例
            int upCount_45 = 0;
            int downCount_45 = 0;
            int upCount_135 = 0;
            int downCount_135 = 0;
            int leftCount = 0;
            int rightCount = 0;
            int upCount = 0;
            int downCount = 0;

            for (int i = 0; i < MaxHorizontalCount + 1; i++)
            {
                for (int j = 0; j < MaxVerticalCount + 1; j++)
                {
                    if (_map[i, j] > 0)
                    {
                        int offsetX = i - startRoom.x;
                        int offsetY = j - startRoom.y;

                        if (offsetY > offsetX) upCount_45++;
                        if (offsetY < offsetX) downCount_45++;
                        if (offsetY < -offsetX) downCount_135++;
                        if (offsetY > -offsetX) upCount_135++;
                        if (offsetX > 0) rightCount++;
                        if (offsetX < 0) leftCount++;
                        if (offsetY > 0) upCount++;
                        if (offsetY < 0) downCount++;
                    }
                }
            }

            if (CheckRoomPercent(upCount_45, downCount_45)
                && CheckRoomPercent(upCount_135, downCount_135))
               // && CheckRoomPercent(leftCount, rightCount)
              //  && CheckRoomPercent(upCount, downCount))
            {
            }
            else
            {
                return false;
            }

            #endregion

            coinRoom = posCoin;
            rewardRoom = posReward;
            normalRooms.AddRange(_endRooms);

            normalRooms.AddRange(_linkRooms);
            normalRooms.Remove(startRoom); //remove start room
            if (normalRooms.Contains(startRoom) || normalRooms.Contains(coinRoom) || normalRooms.Contains(bossRoom) || normalRooms.Contains(rewardRoom))
            {
                Debug.LogError("room data error has same coord");
            }

            return true;
        }

        return false;
    }

    private bool CheckRoomPercent(int left, int right)
    {
        if (left == 0 || right == 0) return true;

        float v1 = (float)left / right;
        float v2 = (float)right / left;

        if ((v1 < _errorMaxPercent && v1 > _errorMinPercent) || (v2 < _errorMaxPercent && v2 > _errorMinPercent)) return false;
        if (v1 < _appropriatePercent || v2 < _appropriatePercent) return true;

        return false;
    }

    public bool CheckAndSetCeil(Vector2Int destPos)
    {
        if (_map[destPos.x, destPos.y] == 1) return false;
        if (NeighbourCount(destPos) > 1) return false;
        if (_currentRoomCount >= MaxRoomCount) return false;
        if (Random.value < 0.5f) return false;
        AddCoordToRoom(destPos);
        return true;
    }

    private void AddCoordToRoom(Vector2Int destPos)
    {
        _rooms.Add(destPos);
        _currentRoomCount += 1;
        _map[destPos.x, destPos.y] = 1;
    }

    private int NeighbourCount(Vector2Int pos)
    {
        return _map[pos.x - 1, pos.y]
               + _map[pos.x + 1, pos.y]
               + _map[pos.x, pos.y - 1]
               + _map[pos.x, pos.y + 1];
    }

    private (bool, Vector2Int) GetRandomEndRoom()
    {
        if (_endRooms.Count == 0) return (false, Vector2Int.zero);
        int index = Random.Range(0, _endRooms.Count - 1);
        Vector2Int result = _endRooms[index];
        _endRooms.RemoveAt(index);
        return (true, result);
    }
}