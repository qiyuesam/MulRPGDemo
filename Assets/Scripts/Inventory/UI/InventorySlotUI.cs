using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventorySlotUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private Image selectBorder;

    private int slotIndex;
    private Inventory inventory;

    // InventoryUI 初始化时调用
    public void Setup(Inventory inv, int index)
    {
        inventory = inv;
        slotIndex = index;
        Refresh();
    }

    // 数据变了刷新显示
    public void Refresh()
    {
        var slot = inventory.GetSlot(slotIndex);
        if (slot.IsEmpty)
        {
            icon.sprite = null;
            icon.color = new Color(1, 1, 1, 0);   // 完全透明
            countText.text = "";
        }
        else
        {
            var item = inventory.GetItemData(slot.itemId);
            icon.sprite = item?.itemIcon;
            icon.color = Color.white;
            countText.text = slot.count > 1 ? slot.count.ToString() : "";
        }
    }

    public void SetSelected(bool selected)
    {
        selectBorder.gameObject.SetActive(selected);
    }

    // Button 点击事件绑定这个
    public void OnClick()
    {
        // 通知 InventoryUI 处理选中/交换逻辑
        InventoryUI.Instance.OnSlotClicked(slotIndex);
    }
}