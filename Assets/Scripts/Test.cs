using System.Text;
using UnityEngine;

public class Test : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        var _dungeon = DungeonGenerator.GenerateDungeon(DungeonSettings.Default);

        var _testdungeon = new StringBuilder();
        for(int i = 0; i < _dungeon.GetLength(0); i++)
        {
            for(int j=0; j < _dungeon.GetLength(1); j++)
            {
                if(_dungeon[i,j]== 1)
                {
                    _testdungeon.Append("□");
                }
                else
                {
                    _testdungeon.Append("■");
                }
            }
            _testdungeon.Append("\n");
        }
        

        Debug.Log(_testdungeon);
    }

   
}
