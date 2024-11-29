using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class AnimProgress : MonoBehaviour
{
    public Slider ProgressSlider;
    public Text ProgressTypeText;
    public void SetProgressType(string type)
    {
        ProgressTypeText.text = type;
    }

    public Text ProgressText;
    void SetProgress(float progress)
    {
        ProgressSlider.value = progress;
        ProgressText.text = $"{progress * 100f:0}%";
    }

    private Coroutine _routineProgress;
    public void ToProgress(float progress)
    {
        if (_routineProgress != null)
        {
            StopCoroutine(_routineProgress);
            _routineProgress = null;
        }

        SetProgress(progress);
    }

    public void AnimToProgress(float progress, float duration = 0.4f)
    {
        if (_routineProgress != null)
        {
            StopCoroutine(_routineProgress);
            _routineProgress = null;
        }

        _routineProgress = StartCoroutine(RoutineProgress(ProgressSlider.value, progress, duration));
    }

    private IEnumerator RoutineProgress(float from, float to, float duration)
    {
        float start = Time.time;
        float end = start + duration;
        float progress = from;
        while (Time.time < end)
        {
            progress = Mathf.Lerp(progress, 1f, (Time.time - start) / duration);
            SetProgress(progress);
            yield return null;
        }

        SetProgress(to);
    }
}