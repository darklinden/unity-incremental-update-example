using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class UpdateAlert : MonoBehaviour
{
    public enum Result
    {
        Undefined,
        Ok,
        Cancel
    }

    public Text TipText;
    public Button OkButton;
    public Text OkButtonText;
    public Button CancelButton;
    public Text CancelButtonText;

    private Result _result = Result.Undefined;
    public async UniTask<Result> AsyncShow(string tip, string okText, string cancelText)
    {
        TipText.text = tip;
        OkButtonText.text = okText;

        if (string.IsNullOrEmpty(cancelText))
        {
            CancelButton.gameObject.SetActive(false);
        }
        else
        {
            CancelButton.gameObject.SetActive(true);
            CancelButtonText.text = cancelText;
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        _result = Result.Undefined;
        while (_result == Result.Undefined)
        {
            await UniTask.Yield();
        }

        gameObject.SetActive(false);

        return _result;
    }

    public void OnClickOk()
    {
        _result = Result.Ok;
    }

    public void OnClickCancel()
    {
        _result = Result.Cancel;
    }
}