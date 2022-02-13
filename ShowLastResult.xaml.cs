using System.Windows;

namespace PlantUMLLiveViewer {
    public partial class ShowLastResult : Window {
        public ShowLastResult() {
            InitializeComponent();
            Title = $"{App.Name} - 최근 결과";

            cResult.Text =
                "출력:\n"
                + $"{App.Inst.GetLastStdOut()}\n"
                + "\n"
                + "오류:\n"
                + $"{App.Inst.GetLastStdErr()}";
        }
    }
}
