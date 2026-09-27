using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MRLab
{
    public sealed class ExperimentUIController : MonoBehaviour
    {
        public ElephantToothpasteExperiment experiment;
        public TMP_Text steps;
        public TMP_Text message;
        public TMP_Text status;
        public TMP_Text selection;
        public Image progressFill;
        public Button resetButton;

        private void Start() { resetButton.onClick.AddListener(experiment.ResetExperiment); }
        private void OnDestroy() { if (resetButton) resetButton.onClick.RemoveListener(experiment.ResetExperiment); }

        private void LateUpdate()
        {
            var session = experiment.Session;
            string content = "";
            for (int i = 0; i < 4; i++)
            {
                bool added = session.Has((Reagent)i);
                string color = added ? "72D9C1" : "D2DFE3";
                string marker = added ? "已加入" : i == 2 ? "可选 · 仅染色" : "可添加";
                content += $"<color=#{color}>{i + 1:00}  {ExperimentSession.ReagentNames[i]}\n<size=17>       {marker}</size></color>\n\n";
            }
            steps.text = content;
            message.text = session.Message;
            status.text = session.Stage == ExperimentStage.Completed
                ? (session.FoamAmount > .03f ? "反应结束  |  泡沫已堆积\n可补加剩余试剂观察，或重置比较其他顺序。" : "反应结束  |  氧气已逸散\n没有稳定泡沫；重置后可比较先加洗洁精的效果。")
                : session.Stage == ExperimentStage.Reacting
                    ? (session.Has(Reagent.Soap) ? "反应中  |  泡沫喷出、下落并堆积" : "反应中  |  产气，但缺少稳定泡沫") + "\n反应期间仍可补加未使用的试剂。"
                    : $"已加入 {session.Step} 种试剂  |  顺序自由，色素可选\n过氧化氢与酵母催化液接触后开始产气。";
            selection.text = !string.IsNullOrEmpty(experiment.HoverName) ? "鼠标指向：" + experiment.HoverName
                : experiment.Selected ? "已选中：" + experiment.Selected.DisplayName : "点击瓶身选择试剂";
            progressFill.fillAmount = session.Progress;
        }
    }
}
