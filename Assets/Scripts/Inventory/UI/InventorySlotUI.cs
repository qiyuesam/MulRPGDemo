// Assets/Scripts/Inventory/UI/InventorySlotUI.cs（修改后，改动标 ★）

using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventorySlotUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private Image selectBorder;

    private int slotIndex;
    // ★ 改1：Inventory → IInventoryProvider
    private IInventoryProvider provider;

    // ★ 改2：参数类型 Inventory → IInventoryProvider
    public void Setup(IInventoryProvider inv, int index)
    {
        provider = inv;
        slotIndex = index;
        Refresh();
    }

    public void Refresh()
    {
        // ★ 改3：inventory → provider
        var slot = provider.GetSlot(slotIndex);
        if (slot.IsEmpty)
        {
            icon.sprite = null;
            icon.color = new Color(1, 1, 1, 0);
            countText.text = "";
        }
        else
        {
            var item = provider.GetItemData(slot.itemId);
            icon.sprite = item?.itemIcon;
            icon.color = Color.white;
            countText.text = slot.count > 1 ? slot.count.ToString() : "";
        }
    }

    public void SetSelected(bool selected)
    {
        selectBorder.gameObject.SetActive(selected);
    }

    public void OnClick()
    {
        InventoryUI.Instance.OnSlotClicked(slotIndex);
    }
}