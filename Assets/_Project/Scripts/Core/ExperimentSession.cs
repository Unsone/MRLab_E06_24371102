using System;

namespace MRLab
{
    public enum Reagent { Peroxide, Soap, Color, Catalyst }
    public enum ExperimentStage { Ready, Reacting, Completed }

    // Finite-batch qualitative chemistry, independent of input and presentation.
    public sealed class ExperimentSession
    {
        public static readonly string[] ReagentNames = { "过氧化氢溶液", "洗洁精", "食用色素", "酵母催化液" };
        public const float InductionDuration = 1f;
        public const float ReactionDuration = 7f; // Includes induction; visual teaching timing, not measured kinetics.
        private readonly bool[] present = new bool[4];
        private Reagent pending;
        private bool committed;
        public ExperimentStage Stage { get; private set; }
        public bool IsAdding { get; private set; }
        public int Step { get; private set; }
        public float ReactionTime { get; private set; }
        public float RemainingPeroxide { get; private set; }
        public float FoamAmount { get; private set; }
        public float DyedFoamAmount { get; private set; }
        public float GasAmount { get; private set; }
        public float Progress => GasAmount;
        public bool IsProducingGas => Stage == ExperimentStage.Reacting && ReactionTime > InductionDuration;
        public string Message { get; private set; }
        public event Action Changed;
        public bool Has(Reagent reagent) => present[(int)reagent];

        public ExperimentSession() { Reset(); }

        public bool TryAdd(Reagent reagent)
        {
            if (IsAdding) { SetMessage("当前正在倾倒，请等待倾倒完成。"); return false; }
            if (Has(reagent)) { SetMessage(ReagentNames[(int)reagent] + "这一份已用完；重置可重新实验。"); return false; }
            pending = reagent;
            committed = false;
            IsAdding = true;
            SetMessage("正在添加" + ReagentNames[(int)reagent] + "……");
            return true;
        }

        // Liquid contact triggers chemistry, before the bottle returns home.
        public void CommitAddition()
        {
            if (!IsAdding || committed) return;
            committed = true;
            present[(int)pending] = true;
            Step++;
            if (pending == Reagent.Peroxide) RemainingPeroxide = 1;
            if (Has(Reagent.Peroxide) && Has(Reagent.Catalyst) && RemainingPeroxide > 0)
                Stage = ExperimentStage.Reacting;
            DescribeContents();
        }

        public void FinishAddition()
        {
            if (!IsAdding) return;
            CommitAddition();
            IsAdding = false;
            DescribeContents();
        }

        public void Tick(float seconds)
        {
            if (Stage != ExperimentStage.Reacting || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            ReactionTime = Math.Min(ReactionDuration, ReactionTime + seconds);
            float activeTime = Math.Max(0, ReactionTime - InductionDuration);
            float remaining = ReactionTime >= ReactionDuration ? 0 : (float)Math.Exp(-.8 * activeTime);
            float produced = Math.Max(0, RemainingPeroxide - remaining);
            RemainingPeroxide = remaining;
            GasAmount += produced;
            if (Has(Reagent.Soap))
            {
                FoamAmount += produced;
                if (Has(Reagent.Color)) DyedFoamAmount += produced;
            }
            if (ReactionTime >= ReactionDuration) { Stage = ExperimentStage.Completed; DescribeContents(); }
            else DescribeContents();
        }

        private void DescribeContents()
        {
            if (Stage == ExperimentStage.Completed)
                SetMessage(FoamAmount > .03f ? "反应结束：泡沫已落下堆积。剩余试剂仍可添加，但不会重新产气。" : "反应结束：氧气已逸散。现在补洗洁精，也不会重新喷出大量泡沫。");
            else if (Stage == ExperimentStage.Reacting && ReactionTime <= InductionDuration)
                SetMessage("反应物已接触，正在积累气体；约 1 秒后开始喷发。");
            else if (Stage == ExperimentStage.Reacting)
                SetMessage(Has(Reagent.Soap) ? "正在产气：洗洁精稳定气泡，连续泡沫冲出瓶口后落下堆积。" : "正在分解产气：没有洗洁精，气泡容易破裂，难以形成稳定泡沫柱。");
            else if (!Has(Reagent.Peroxide)) SetMessage("可自由添加。容器内还没有过氧化氢，不会发生本实验的产气反应。");
            else SetMessage("过氧化氢已加入；加入酵母催化液后会明显产气。洗洁精用于稳定泡沫，色素只染色。");
        }

        public void SetMessage(string message) { Message = message; Changed?.Invoke(); }
        public void Reset()
        {
            Array.Clear(present, 0, present.Length);
            Stage = ExperimentStage.Ready;
            IsAdding = committed = false;
            Step = 0;
            ReactionTime = RemainingPeroxide = FoamAmount = DyedFoamAmount = GasAmount = 0;
            SetMessage("任意顺序添加试剂。色素可选；先选试剂，再点击烧瓶。");
        }
    }
}
