using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using Random = UnityEngine.Random;

public class TestRoomGenerator_Multi : MonoBehaviour
{
    [SerializeField] private MultiRoomDataGenerator _roomDataGenerator = new();
    [SerializeField] private Vector2 cellSize = new Vector2(100, 80);
    [SerializeField] private GameObject _startPrefab;
    [SerializeField] private GameObject _normalPrefab;
    [SerializeField] private GameObject _bossPrefab;
    [TableList] [SerializeField] private SerializableDictionary<ShapeEnum, GameObject> _specialPrefabs = new();
    [SerializeField] private Transform _parentTrans;

    private void OnGUI()
    {
        if (GUI.Button(new Rect(0, 0, 300, 100), "Gen"))
        {
            DestroyRooms();
            bool success = _roomDataGenerator.GenerateRooms();

            if (success)
            {
                //start
                GameObject go = Instantiate(_startPrefab, _parentTrans);
                go.GetComponent<RectTransform>().anchoredPosition = GetRoomRectPos(_roomDataGenerator.startRoom);

                //special shape
                foreach (var shapeRoomInfo in _roomDataGenerator.shapeRooms)
                {
                    ShapeEnum shapeEnum = shapeRoomInfo.shapeEnum;
                    Vector2Int leftDownCoord = shapeRoomInfo.shapeLeftDownCoord;
                    go = Instantiate(_specialPrefabs[shapeEnum], _parentTrans);
                    go.GetComponent<RectTransform>().anchoredPosition = GetSpecialRectPos(shapeEnum, leftDownCoord);
                }

                //normal这里包含了非boss的特殊房间
                foreach (var room in _roomDataGenerator.allNormalRooms)
                {
                    go = Instantiate(_normalPrefab, _parentTrans);
                    go.GetComponent<RectTransform>().anchoredPosition = GetRoomRectPos(room);
                }

                //boss
                go = Instantiate(_bossPrefab, _parentTrans);
                go.GetComponent<RectTransform>().anchoredPosition = GetRoomRectPos(_roomDataGenerator.bossRoom);
            }
        }
    }

    private Vector2 GetRoomRectPos(Vector2Int roomPos)
    {
        Vector2Int startPos = _roomDataGenerator.startRoom;
        return new Vector2((roomPos.x - startPos.x) * cellSize.x, (roomPos.y - startPos.y) * cellSize.y);
    }

    private Vector2 GetSpecialRectPos(ShapeEnum shapeEnum, Vector2Int roomPos)
    {
        var pos = GetRoomRectPos(roomPos);
        switch (shapeEnum)
        {
            case ShapeEnum.Large:
            case ShapeEnum.L1:
                return pos + cellSize * 0.5f;
        }

        Debug.LogError($"shapeEnum={shapeEnum} error");
        return Vector2.zero;
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
public class MultiRoomDataGenerator
{
    [Title("房间生成参数:")] [InfoBox("额外扩展Map的格子数，确保Map数组不会越界，因为有MultiShape的存在，所以需要额外扩展，目前>2")] [SerializeField]
    private int ExtraExtendCount = 5;

    [InfoBox("MaxCount和RoomCount比例暂定为10:7")] [SerializeField]
    private int MaxHorizontalCount;

    [SerializeField] private int MaxVerticalCount;

    [InfoBox("尽量MinRoomCount <=MaxRoomCount -2")] [SerializeField]
    private int MinRoomCount;

    [SerializeField] private int MaxRoomCount;
    [SerializeField] private int SafeCount = 1000;

    [InfoBox("非NormalShapes的房间")] [SerializeField]
    private List<ShapeEnum> _specialShapeEnums = new List<ShapeEnum>();

    [Title("---检测区域---")] [InfoBox("v1 v2分别代表分子和分母")] [SerializeField]
    private float _maxAppropriatePercentV1 = 3.0f;

    [SerializeField] private float _maxAppropriatePercentV2 = 7.0f;
    private float _maxAppropriatePercent = 3.0f / 7.0f;

    [SerializeField] private float _minAppropriatePercentV1 = 0f;
    [SerializeField] private float _minAppropriatePercentV2 = 10f;
    private float _minAppropriatePercent = 0;

    [Title("StartRoom周围的补充:")] [SerializeField] [InfoBox("start房间邻接只有一个房间时, 增加房间的概率 [0]:增加一个房间 [1]增加2个房间 [2]增加3个房间")]
    private float[] _startNeighbourPercent = { 0.85f, 0.5f, 0.15f };

    [SerializeField] [InfoBox("start房间邻接有两个房间时, 邻接房间生成的概率 [0]:增加1个房间 [1]增加2个房间")]
    private float[] _startNeighbourPercent_Second = { 0.3f, 0.15f };


    private Map _map;
    public Vector2Int startRoom { get; private set; }
    public Vector2Int bossRoom { get; private set; }
    public List<ShapeRoomInfo> shapeRooms { get; private set; } = new List<ShapeRoomInfo>();
    public List<Vector2Int> allNormalRooms { get; private set; } = new List<Vector2Int>();
    public List<Vector2Int> endRooms { get; private set; } = new List<Vector2Int>();
    private List<Vector2Int> _linkRooms = new();
    private List<Vector2Int> _dynamicCells = new List<Vector2Int>();
    private int _currentRoomCount;
    private int _currentGenerateTimer;
    private List<ShapeEnum> _allShapeEnumPool = new List<ShapeEnum>();

    public void Init()
    {
        _maxAppropriatePercent = _maxAppropriatePercentV1 / _maxAppropriatePercentV2;
        _minAppropriatePercent = _minAppropriatePercentV1 / _minAppropriatePercentV2;
        if (_map == null)
        {
            _map = new Map(MaxHorizontalCount, MaxVerticalCount, ExtraExtendCount);
            startRoom = new Vector2Int(Mathf.FloorToInt((MaxHorizontalCount + ExtraExtendCount) * 0.5f), Mathf.FloorToInt((MaxVerticalCount + ExtraExtendCount) * 0.5f));
            Debug.Log($"startRoom = {startRoom}");
        }

        _map.Reset();
        shapeRooms.Clear();
        allNormalRooms.Clear();
        endRooms.Clear();
        _linkRooms.Clear();
        _dynamicCells.Clear();
        _currentRoomCount = 0;
        InitShapeInfo();
        //set start room to _rooms
        DirectlyAddCoordToMap(startRoom);
        //
    }

    public bool GenerateRooms()
    {
        _currentGenerateTimer = 0;
        while (_currentGenerateTimer < SafeCount)
        {
            _currentGenerateTimer++;
            Init();
            if (GenerateRooms_Once())
            {
                return true;
            }
        }

        //TODO(fei):如果超过SafeCount，说明算法有问题。或者额外要做一个模版功能,简单点直接生成L形状地图就好了。
        Debug.LogError($" _currentGenerateCount > {SafeCount} pls check algorithm");

        return false;
    }

    private bool GenerateRooms_Once()
    {
        #region 房间从startRoom开始延伸

        while (_dynamicCells.Count > 0)
        {
            Vector2Int currentRoom = _dynamicCells[0];
            bool isCreated = false;
            if (currentRoom.x > 1 && CheckAndSetCeil(FillDirection.Left, currentRoom + Vector2Int.left)) isCreated = true;
            if ((currentRoom.x < MaxHorizontalCount - 1) && CheckAndSetCeil(FillDirection.Right, currentRoom + Vector2Int.right)) isCreated = true;
            if (currentRoom.y > 1 && CheckAndSetCeil(FillDirection.Down, currentRoom + Vector2Int.down)) isCreated = true;
            if ((currentRoom.y < MaxVerticalCount - 1) && CheckAndSetCeil(FillDirection.Up, currentRoom + Vector2Int.up)) isCreated = true;

            //specialShapeRoom需要单独处理，排除在allNormalRooms之外
            if (_map.GetCellValue(currentRoom) == 1)
            {
                if (!isCreated)
                {
                    endRooms.Add(currentRoom);
                }
                else
                {
                    _linkRooms.Add(currentRoom);
                }
            }

            _dynamicCells.RemoveAt(0);
        }

        #endregion

        if (_currentRoomCount < MinRoomCount || endRooms.Count == 0) return false;

        if (shapeRooms.Count < _specialShapeEnums.Count) return false;
        
        bossRoom = endRooms[^1];
        endRooms.RemoveAt(endRooms.Count - 1);
        if (endRooms.Count < 2) return false;

        #region 所有房间生成后,进行四次区域块房间个数百分比判断,如果不符合百分比Condition,则重新生成

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
                if (_map.GetMaskValue(i, j) > 0)
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
            && CheckRoomPercent(upCount_135, downCount_135)
            && CheckRoomPercent(leftCount, rightCount)
            && CheckRoomPercent(upCount, downCount))
        {
            //doNothing
        }
        else
        {
            return false;
        }

        #endregion

        #region 补充:如果start房间相邻房间个数 == 1或2, 随机给start增加相邻endRooms

        var emptyNeighboursRooms = _map.GetEmptyNeighboursPos(startRoom);
        int emptyNeighboursCount = emptyNeighboursRooms.Count;
        int fullNeighbourCount = 4 - emptyNeighboursCount;
        int startNeighbourAddCount = 0;

        float randomValue = Random.value;
        if (fullNeighbourCount == 1)
        {
            int index = 0;
            for (var i = _startNeighbourPercent.Length - 1; i >= 0; --i)
            {
                if (randomValue >= _startNeighbourPercent[i])
                {
                    startNeighbourAddCount = i + 1;
                }
            }
        }
        else if (fullNeighbourCount == 2)
        {
            int index = 0;
            for (var i = _startNeighbourPercent_Second.Length - 1; i >= 0; --i)
            {
                if (randomValue >= _startNeighbourPercent_Second[i])
                {
                    startNeighbourAddCount = i + 1;
                }
            }
        }

        for (int i = 0; i < startNeighbourAddCount; i++)
        {
            int count = emptyNeighboursRooms.Count;
            int randomIndex = Random.Range(0, count);
            Vector2Int randomRoomPos = emptyNeighboursRooms[randomIndex];
            emptyNeighboursRooms.RemoveAt(randomIndex);
            DirectlyAddCoordToMap(randomRoomPos);
            endRooms.Add(randomRoomPos);
        }

        #endregion

        allNormalRooms.AddRange(endRooms);
        allNormalRooms.AddRange(_linkRooms);
        allNormalRooms.Remove(startRoom); //remove start room

        return true;
    }

    #region 生成房间

    private void InitShapeInfo()
    {
        _allShapeEnumPool.Clear();
        for (int i = 0; i < MinRoomCount; i++)
        {
            if (i < _specialShapeEnums.Count)
            {
                _allShapeEnumPool.Add(_specialShapeEnums[i]);
            }
            else
            {
                _allShapeEnumPool.Add(ShapeEnum.Normal);
            }
        }
    }

    private ShapeEnum RandomShapeEnum()
    {
        if (_allShapeEnumPool.Count == 0) return ShapeEnum.Normal;
        int index = Random.Range(0, _allShapeEnumPool.Count);
        ShapeEnum shapeEnum = _allShapeEnumPool[index];
        _allShapeEnumPool.RemoveAt(index);
        return shapeEnum;
    }

    private bool CheckAndSetCeil(FillDirection relativeDir, Vector2Int destPos)
    {
        if (_map.GetMaskValue(destPos) > 0) return false;
        if (_currentRoomCount >= MaxRoomCount) return false;

        ShapeEnum shapeEnum = RandomShapeEnum();

        if (shapeEnum == ShapeEnum.Normal)
        {
            if (Random.value < 0.5f) return false;
            if (ShapeCheckAndFillFactory.CheckAndFill_Normal(_map, _dynamicCells, relativeDir, destPos))
            {
                _currentRoomCount++;
                return true;
            }
        }
        else
        {
            if (ShapeCheckAndFillFactory.CheckAndFill_SpecialShape(shapeEnum, _map, _dynamicCells, shapeRooms, relativeDir, destPos))
            {
                _currentRoomCount++;
                return true;
            }
        }

        return false;
    }

    private void DirectlyAddCoordToMap(Vector2Int destPos)
    {
        NormalShape.FillNormalCell(_map, _dynamicCells, destPos);
        _currentRoomCount++;
    }

    #endregion


    #region 百分比

    private bool CheckRoomPercent(int left, int right)
    {
        if (left == 0 || right == 0) return true;

        float v1 = (float)left / right;
        float v2 = (float)right / left;

        //Debug.Log($"CheckRoomPercent v1:{v1} v2:{v2} ");
        return (v1 <= _maxAppropriatePercent && v1 >= _minAppropriatePercent)
               || (v2 <= _maxAppropriatePercent && v2 >= _minAppropriatePercent);
    }

    #endregion
}

/// <summary>
/// 针对multiRoom的房间生成器
/// 需要判断填充的方向
/// </summary>
public enum FillDirection
{
    Up = 1,
    Down = 2,
    Left = 3,
    Right = 4,
}

public class Map
{
    private int[,] _data;
    private int _horizontalCount;
    private int _verticalCount;
    private int _extendCount;

    public Map(int horizontalCount, int verticalCount, int extendCount)
    {
        _horizontalCount = horizontalCount;
        _verticalCount = verticalCount;
        _extendCount = extendCount;
        _data = new int[horizontalCount + _extendCount, verticalCount + _extendCount];
    }

    public void Reset()
    {
        for (int i = 0; i < _horizontalCount + _extendCount; i++)
        {
            for (int j = 0; j < _verticalCount + _extendCount; j++)
            {
                _data[i, j] = 0;
            }
        }
    }

    /// <summary>
    /// 1:Filled 已填充
    /// 0:Empty  未填充
    /// </summary>
    public int GetMaskValue(Vector2Int coord)
    {
        return _data[coord.x, coord.y] > 0 ? 1 : 0;
    }
    
    /// <summary>
    /// 1:Filled 已填充
    /// 0:Empty  未填充
    /// </summary>
    public int GetMaskValue(int x, int y)
    {
        return _data[x, y] > 0 ? 1 : 0;
    }

    /// <summary>
    /// 获取cell的值
    /// 可以区分不同的Shape
    /// </summary>
    public int GetCellValue(Vector2Int coord)
    {
        return _data[coord.x, coord.y];
    }

    /// <summary>
    /// 指定位置填充
    /// 填充后NormalShape:1
    /// 其他Shape暂定义为2(目前在数据层还不需要区分不同Shape)
    /// </summary>
    /// <param name="coord"></param>
    /// <param name="value"></param>
    public void FillCell(Vector2Int coord, int value)
    {
        _data[coord.x, coord.y] = value;
    }

    public List<Vector2Int> GetEmptyNeighboursPos(Vector2Int pos)
    {
        List<Vector2Int> result = new List<Vector2Int>();

        if (_data[pos.x - 1, pos.y] == 0) result.Add(pos + Vector2Int.left);
        if (_data[pos.x + 1, pos.y] == 0) result.Add(pos + Vector2Int.right);
        if (_data[pos.x, pos.y - 1] == 0) result.Add(pos + Vector2Int.down);
        if (_data[pos.x, pos.y + 1] == 0) result.Add(pos + Vector2Int.up);
        return result;
    }
}

public class NormalShape
{
    public static void FillNormalCell(Map map, List<Vector2Int> dynamicCells, Vector2Int destCoord)
    {
        map.FillCell(destCoord, 1);
        dynamicCells.Add(destCoord);
    }

    public static bool CheckAndFill(Map map, List<Vector2Int> dynamicCells, FillDirection dir, Vector2Int destCoord)
    {
        int v = map.GetMaskValue(destCoord + Vector2Int.left);
        v += map.GetMaskValue(destCoord + Vector2Int.right);
        v += map.GetMaskValue(destCoord + Vector2Int.up);
        v += map.GetMaskValue(destCoord + Vector2Int.down);

        if (v > 1) return false;

        FillNormalCell(map, dynamicCells, destCoord);
        return true;
    }
}

public struct ShapeRoomInfo
{
    public ShapeEnum shapeEnum { get; }
    public Vector2Int shapeLeftDownCoord { get; }

    public ShapeRoomInfo(ShapeEnum shapeEnum, Vector2Int shapeLeftDownCoord)
    {
        this.shapeEnum = shapeEnum;
        this.shapeLeftDownCoord = shapeLeftDownCoord;
    }
}

/// <summary>
/// 2*2 四方形
///  1,1
///  1,1
/// </summary>
public class LargeShape
{
    public static bool CheckAndFill(Map map, List<Vector2Int> dynamicCells, List<ShapeRoomInfo> shapeCoords, FillDirection dir, Vector2Int destCoord)
    {
        Vector2Int coord1 = Vector2Int.zero;
        Vector2Int coord2 = Vector2Int.zero;
        Vector2Int coord3 = Vector2Int.zero;

        Vector2Int shapeLeftDownCoord = Vector2Int.zero;
        switch (dir)
        {
            case FillDirection.Right:
            case FillDirection.Up:
                coord1 = destCoord + Vector2Int.right;
                coord2 = destCoord + Vector2Int.up;
                coord3 = destCoord + Vector2Int.up + Vector2Int.right;
                shapeLeftDownCoord = destCoord;
                break;
            case FillDirection.Left:
                coord1 = destCoord + Vector2Int.left;
                coord2 = destCoord + Vector2Int.up;
                coord3 = destCoord + Vector2Int.up + Vector2Int.left;
                shapeLeftDownCoord = destCoord + Vector2Int.left;
                break;
            case FillDirection.Down:
                coord1 = destCoord + Vector2Int.right;
                coord2 = destCoord + Vector2Int.down;
                coord3 = destCoord + Vector2Int.down + Vector2Int.right;
                shapeLeftDownCoord = destCoord + Vector2Int.down;
                break;
        }

        int v = map.GetMaskValue(coord1) + map.GetMaskValue(coord2) + map.GetMaskValue(coord3);
        if (v > 0) return false;

        map.FillCell(destCoord, 2);
        map.FillCell(coord1, 2);
        map.FillCell(coord2, 2);
        map.FillCell(coord3, 2);

        dynamicCells.Add(destCoord);
        dynamicCells.Add(coord1);
        dynamicCells.Add(coord2);
        dynamicCells.Add(coord3);

        shapeCoords.Add(new ShapeRoomInfo(ShapeEnum.Large, shapeLeftDownCoord));
        return true;
    }
}

/// <summary>
/// L1形状
/// 1,0
/// 1,1
/// </summary>
public class L1Shape
{
    public static bool CheckAndFill(Map map, List<Vector2Int> dynamicCells, List<ShapeRoomInfo> shapeCoords, FillDirection dir, Vector2Int destCoord)
    {
        Vector2Int coord1 = Vector2Int.zero;
        Vector2Int coord2 = Vector2Int.zero;
        Vector2Int shapeLeftDownCoord = Vector2Int.zero;

        switch (dir)
        {
            case FillDirection.Right:
            case FillDirection.Up:
                coord1 = destCoord + Vector2Int.right;
                coord2 = destCoord + Vector2Int.up;
                shapeLeftDownCoord = destCoord;
                break;
            case FillDirection.Left:
                coord1 = destCoord + Vector2Int.left;
                coord2 = destCoord + Vector2Int.up + Vector2Int.left;
                shapeLeftDownCoord = destCoord + Vector2Int.left;
                break;
            case FillDirection.Down:
                coord1 = destCoord + Vector2Int.down;
                coord2 = destCoord + Vector2Int.down + Vector2Int.right;
                shapeLeftDownCoord = destCoord + Vector2Int.down;
                break;
        }

        int v = map.GetMaskValue(coord1) + map.GetMaskValue(coord2);
        if (v > 0) return false;

        map.FillCell(destCoord, 2);
        map.FillCell(coord1, 2);
        map.FillCell(coord2, 2);
        dynamicCells.Add(destCoord);
        dynamicCells.Add(coord1);
        dynamicCells.Add(coord2);
        shapeCoords.Add(new ShapeRoomInfo(ShapeEnum.L1, shapeLeftDownCoord));
        return true;
    }
}

public enum ShapeEnum
{
    Normal = 1,
    Large = 2,
    L1 = 3,
}

public class ShapeCheckAndFillFactory
{
    public static bool CheckAndFill_Normal(Map map, List<Vector2Int> ceils, FillDirection dir, Vector2Int destCoord)
    {
        return NormalShape.CheckAndFill(map, ceils, dir, destCoord);
    }

    public static bool CheckAndFill_SpecialShape(ShapeEnum shapeEnum, Map map, List<Vector2Int> dynamicCells, List<ShapeRoomInfo> shapeCoords, FillDirection dir, Vector2Int destCoord)
    {
        switch (shapeEnum)
        {
            case ShapeEnum.Large:
                return LargeShape.CheckAndFill(map, dynamicCells, shapeCoords, dir, destCoord);
            case ShapeEnum.L1:
                return L1Shape.CheckAndFill(map, dynamicCells, shapeCoords, dir, destCoord);
            default:
                Debug.LogError($"cannot found shape={shapeEnum}");
                return false;
        }
    }
}