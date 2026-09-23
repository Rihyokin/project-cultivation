using UnityEngine;

/// <summary>
/// 进游戏场景后，把主菜单选中的那份存档套到角色身上。
/// 挂在 Player 上，Start 里跑一次。
/// </summary>
public class SaveApplier : MonoBehaviour
{
    void Start()
    {
        var 数据 = SaveSystem.当前存档;
        if (数据 == null)
        {
            // 直接从游戏场景启动（没经过主菜单）时，自动读最近用过的槽位
            for (int i = 0; i < SaveSystem.槽位数; i++)
                if (SaveSystem.有存档(i))
                {
                    数据 = SaveSystem.读档(i);
                    SaveSystem.当前槽位 = i;
                    Debug.Log("[SaveApplier] 未经主菜单启动，自动载入槽位 " + i);
                    break;
                }
        }

        if (数据 == null) return;
        SaveSystem.应用到角色(数据);
        Debug.Log("[SaveApplier] 已应用存档：" + 数据.角色名 + " / " + 数据.境界);
    }
}
