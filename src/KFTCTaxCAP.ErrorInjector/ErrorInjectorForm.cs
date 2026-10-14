using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KFTCTaxCAP.ErrorInjector
{
    /// <summary>
    /// 오류 주입 도구 화면. 원래 KioskSim MainForm의 "오류 주입" 탭이었던 코드를 그대로 옮겼다
    /// (KioskSim 업체 전달용 정리, 2026-10-14). 각 행: [실행 버튼] [기대 결과] [실제 결과].
    /// 본 앱(KFTCTaxCAP)이 127.0.0.1:8002에서 떠 있어야 한다.
    /// </summary>
    public class ErrorInjectorForm : Form
    {
        /// <summary>시나리오 번호 → 결과를 보여줄 라벨. 버튼 클릭 시 이 라벨을 갱신한다.</summary>
        private readonly Dictionary<int, Label> _errorScenarioResultLabels = new Dictionary<int, Label>();

        /// <summary>시나리오 번호 → 실행 버튼. 실행 중에는 그 버튼만 비활성화한다(다른 시나리오는
        /// 동시에 눌러도 무방하다 — 각자 독립된 소켓을 쓴다).</summary>
        private readonly Dictionary<int, Button> _errorScenarioButtons = new Dictionary<int, Button>();

        public ErrorInjectorForm()
        {
            Text = "KFTCTaxCAP 오류 주입 도구 (내부 전용)";
            Width = 1360;
            Height = 980;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("맑은 고딕", 9F);

            BuildScenarioView();
        }

        /// <summary>
        /// 화면 구성(Phase 19 실행계획서 P19-7). 시나리오를 위→아래로 나열한다.
        /// 각 행: [실행 버튼] [기대 결과(정적 텍스트, 실행 전에도 보임)] [실제 결과(실행 후 갱신)].
        /// 업체가 자기 서버를 만들 때 그대로 체크리스트로 쓸 수 있도록 "기대 결과"를 코드에 박아
        /// 항상 화면에 보이게 한다(development_plan.md P19-7 요구사항).
        /// </summary>
        private void BuildScenarioView()
        {
            var headerLabel = new Label
            {
                // 2026-09-17 사용자 지적(5차, 근본 원인) — Label.AutoSize 기본값이 true라서, 아래
                // Height=54를 명시해도 실제로는 텍스트 내용에 맞춰 그보다 더 크게(또는 줄바꿈 방식에
                // 따라 다르게) 스스로 재계산해 버린다. 그 결과 이 라벨이 자기 선언 높이보다 실제로
                // 더 많은 공간을 차지하면서, 바로 아래 Dock=Fill인 scenarioScrollHost와 겹쳐 1번
                // 시나리오 행 위쪽이 가려지는 게 진짜 원인이었다(스크롤 컨테이너를 세 가지 다른
                // 방식으로 바꿔도 동일하게 재현된 이유 — 스크롤 로직과 무관한 문제였다). AutoSize를
                // 명시적으로 꺼서 선언한 Height를 그대로 지키게 한다.
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 54,
                Padding = new Padding(8),
                Font = new Font("맑은 고딕", 9F, FontStyle.Bold),
                Text = "이 도구는 잘못된 프로토콜/네트워크 상황을 일부러 만들어 본 앱(KFTCTaxCAP)의 방어 동작을 " +
                       "확인한다. ErrorInjectionClient.cs가 로우레벨 TCP 소켓으로 직접 구현했다(정상 " +
                       "경로 OneCapClient는 재사용하지 않는다 — 완성된 프레임만 다루도록 설계돼 있어 여기 " +
                       "맞지 않는다).",
            };
            // 2026-09-17 사용자 지적(2~5차) — Panel.AutoScroll 기반 시도를 네 가지 다른 조합
            // (Dock=Top+AutoSize / 수동 Location+AutoSize / Dock=Fill+MinimumSize / 수동
            // Location+명시적 Size)으로 바꿔봤지만 전부 동일하게 1번 행 위쪽이 잘렸다 — 심지어
            // 창을 최대화해 콘텐츠가 표시 영역보다 작아지는 경우에도 재현됐다(스크롤 자체가 필요
            // 없어야 하는 상황인데도 잘림). 원인이 WinForms의 AutoScroll 내부 계산 자체에 있는
            // 것으로 보여, AutoScroll을 아예 쓰지 않고 VScrollBar를 직접 붙여 스크롤 위치를
            // scenarioPanel.Top에 수동으로 대입하는 방식으로 바꾼다 — 애매한 자동 계산에 기대지
            // 않는 가장 확실한 방법이다.
            var scenarioScrollHost = new Panel { Dock = DockStyle.Fill };

            // 2026-09-17 사용자 지적(6차, 근본 원인) — headerLabel(Dock=Top)을 scenarioScrollHost
            // (Dock=Fill)보다 먼저 Controls에 추가했더니 1번 행이 계속 가려졌다. AutoSize=false로
            // 바꿔도(5차) 소용없었던 이유가 이거였다 — WinForms Dock 레이아웃은 Controls 컬렉션에
            // **나중에** 추가된 컨트롤을 먼저 도킹시킨다(z-order 최상단이 우선). Dock=Fill을
            // 나중에 추가하면 Dock=Fill이 자신을 부모 전체로 채운 뒤에야 Dock=Top이 그 위에 다시
            // 얹혀 잘리는 문제가 생긴다 — 그래서 Dock=Fill(scenarioScrollHost)을 먼저 추가하고
            // Dock=Top(headerLabel)을 나중에 추가해야 Top이 진짜로 위쪽 공간을 먼저 차지한다.
            Controls.Add(scenarioScrollHost);
            Controls.Add(headerLabel);

            var scenarioVScroll = new VScrollBar { Dock = DockStyle.Right };
            scenarioScrollHost.Controls.Add(scenarioVScroll);

            var scenarioPanel = new TableLayoutPanel
            {
                Location = new Point(0, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ColumnCount = 3,
                RowCount = 10,
                Padding = new Padding(8),
            };
            scenarioScrollHost.Controls.Add(scenarioPanel);
            scenarioPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            scenarioPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 480));
            scenarioPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 10; i++)
                scenarioPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));

            AddErrorScenarioRow(scenarioPanel, 0, 1, "1. 선언 길이 ≠ 실제 본문 길이",
                "501008(정상 706바이트)을 길이 헤더 \"0700\"으로 선언하고 딱 그만큼(700바이트)만 보낸다.\n" +
                "기대: #7 응답 코드 = E40(길이 불일치).",
                () => ErrorInjectionClient.Scenario1_DeclaredLengthMismatch());

            AddErrorScenarioRow(scenarioPanel, 1, 2, "2. 알 수 없는 거래 구분 코드(#4)",
                "501008 프레이밍은 정상이되 #4(거래 구분 코드)에 존재하지 않는 \"999900\"을 넣어 보낸다.\n" +
                "기대: #7 응답 코드 = E41(알 수 없는 거래구분).",
                () => ErrorInjectionClient.Scenario2_UnknownTransactionType());

            AddErrorScenarioRow(scenarioPanel, 2, 3, "3. 길이 필드가 숫자가 아님",
                "길이 헤더 4바이트에 \"abcd\"를 넣고 아무 본문이나 뒤에 붙여 보낸다.\n" +
                "기대: #7 응답 코드 = E43(전문 형식 오류)을 회신한 뒤 그 연결을 닫는다(Phase 27 P27-8-f).",
                () => ErrorInjectionClient.Scenario3_NonNumericLengthHeader());

            AddErrorScenarioRow(scenarioPanel, 3, 4, "4. 본문을 나눠 보내기",
                "정상 501008 프레임(710바이트)을 100바이트씩 조각내 조각 사이에 짧은 지연을 두고 보낸다.\n" +
                "기대: 서버 프레이머가 부분 수신을 누적해 정상 응답한다.",
                () => ErrorInjectionClient.Scenario4_ChunkedSend());

            AddErrorScenarioRow(scenarioPanel, 4, 5, "5. 응답 전 연결 강제 종료",
                "정상 501008 요청을 보낸 직후 응답을 기다리지 않고 소켓을 바로 닫는다. 이어서 자동으로\n" +
                "정상 501008을 하나 더 보낸다. 기대: 서버가 죽지 않고 다음 요청을 정상 처리한다.",
                () => ErrorInjectionClient.Scenario5_AbortBeforeResponse());

            AddErrorScenarioRow(scenarioPanel, 5, 6, "6. 응답을 읽지 않고 붙들기",
                "정상 501008 요청을 보내고 연결은 열어 둔 채 응답을 7초간 절대 Read하지 않다가 닫는다.\n" +
                "이어서 자동으로 정상 501008을 하나 더 보낸다. 기대: 서버 송신 타임아웃(5초, " +
                "PosSocketServer.SendTimeoutMilliseconds)이 지나도 그 뒤 요청은 막히지 않는다.",
                () => ErrorInjectionClient.Scenario6_HoldResponseUnread());

            AddErrorScenarioRow(scenarioPanel, 6, 7, "7. 연속 2건 즉시 전송",
                "501008 두 개를 서로 다른 연결로 거의 동시에 보낸다.\n" +
                "기대: 단일 워커 큐가 직렬화해서 둘 다 정상 응답한다.",
                () => ErrorInjectionClient.Scenario7_ConcurrentRequests());

            AddErrorScenarioRow(scenarioPanel, 7, 8, "8. 버퍼 상한 초과",
                "길이 헤더에 \"9999\"를 선언하고 그 뒤로 완성되지 않는 쓰레기 바이트를 64KB(65536바이트)\n" +
                "넘게 계속 보낸다. 기대: 서버가 E41/E43 응답을 회신한 뒤 연결을 닫는다(헤더 최대값이 9999라 실제 트리거는 비숫자 헤더).",
                () => ErrorInjectionClient.Scenario8_BufferOverflowAttempt());

            AddErrorScenarioRow(scenarioPanel, 8, 9, "9. 조회 응답 유실 → 재연결 복구(999999, Phase 26)",
                "정상 501008을 보내 성공(#9 확보) → 같은 #9로 조회(999999) 요청을 보내고 응답을 한 바이트도\n" +
                "읽지 않고 즉시 연결을 끊는다 → 재연결해 같은 조회를 다시 보낸다. 기대: #7=\"000\", #14=\"501008\",\n" +
                "꼬리가 ①의 501008 응답 원문과 바이트 단위로 일치(이 Phase의 존재 이유 재현).",
                () => ErrorInjectionClient.Scenario9_InquiryResponseLossRecovery());

            AddErrorScenarioRow(scenarioPanel, 9, 10, "10. 본문 16바이트 미만(#4 식별 불가)",
                "길이 헤더는 정상(프레이밍은 안 깨짐)이되 본문을 10바이트만 보내 #4(거래 구분 코드)조차\n" +
                "읽지 못하게 한다. 기대: #7=E42, #4=\"000000\"(placeholder) 응답을 회신하고 연결은 유지한다 —\n" +
                "이어서 같은 연결로 보낸 정상 501008 요청도 정상 처리된다(Phase 27 P27-8-f).",
                () => ErrorInjectionClient.Scenario10_TooShortBodyMissingTransactionType());

            // scenarioPanel의 실제 필요 높이 — 폭은 컬럼 정의(Absolute 220+480, 나머지는 Percent라
            // 실제 렌더 폭은 부모 폭에 따라 달라지지만 최초 폭은 이 정도면 충분하다 — Anchor에 Right가
            // 걸려 있어 창 크기가 바뀌면 그에 맞춰 늘어난다) 높이는 RowStyles(전부 Absolute) 합산 +
            // 패딩으로 정확히 계산한다.
            int totalRowsHeight = 0;
            foreach (RowStyle rowStyle in scenarioPanel.RowStyles)
                totalRowsHeight += (int)rowStyle.Height;
            int requiredHeight = scenarioPanel.Padding.Top + scenarioPanel.Padding.Bottom + totalRowsHeight;
            scenarioPanel.Size = new Size(1300, requiredHeight);

            // VScrollBar 수동 배선 — scenarioPanel.Top에 음수값을 직접 대입해 스크롤한다. 뷰포트
            // 높이(scenarioScrollHost.ClientSize.Height)는 창이 리사이즈될 때마다 바뀌므로, 스크롤
            // 가능 범위(Maximum)도 그때마다 다시 계산한다.
            void UpdateScrollRange()
            {
                int viewportHeight = scenarioScrollHost.ClientSize.Height;
                int overflow = Math.Max(0, requiredHeight - viewportHeight);
                scenarioVScroll.Minimum = 0;
                scenarioVScroll.SmallChange = 30;
                scenarioVScroll.LargeChange = Math.Max(1, viewportHeight);
                // VScrollBar.Maximum은 "스크롤 가능한 마지막 값 + LargeChange - 1"이어야 실제로
                // overflow까지 도달한다(WinForms ScrollBar의 잘 알려진 관례).
                scenarioVScroll.Maximum = overflow + scenarioVScroll.LargeChange - 1;
                scenarioVScroll.Enabled = overflow > 0;
                if (scenarioVScroll.Value > overflow)
                    scenarioVScroll.Value = overflow;
                scenarioPanel.Top = -scenarioVScroll.Value;
            }

            scenarioVScroll.Scroll += (s, e) => scenarioPanel.Top = -e.NewValue;
            scenarioScrollHost.Resize += (s, e) => UpdateScrollRange();
            scenarioScrollHost.MouseWheel += (s, e) =>
            {
                int step = (e.Delta / 120) * scenarioVScroll.SmallChange;
                int newValue = scenarioVScroll.Value - step;
                newValue = Math.Max(scenarioVScroll.Minimum, Math.Min(scenarioVScroll.Maximum - scenarioVScroll.LargeChange + 1, newValue));
                scenarioVScroll.Value = newValue;
                scenarioPanel.Top = -newValue;
            };
            UpdateScrollRange();
        }

        /// <summary>시나리오 한 행(버튼 + 기대 결과 + 실제 결과 라벨)을 만들어 테이블에 추가한다.</summary>
        private void AddErrorScenarioRow(TableLayoutPanel panel, int row, int scenarioNumber, string title,
            string expectedText, Func<string> runScenario)
        {
            var button = new Button
            {
                Text = title,
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
            };

            var expectedLabel = new Label
            {
                Text = "기대 결과: " + expectedText,
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                AutoSize = false,
            };

            var resultLabel = new Label
            {
                Text = "(아직 실행하지 않음)",
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                AutoSize = false,
                ForeColor = Color.Gray,
            };

            button.Click += async (s, e) => await RunErrorScenarioAsync(scenarioNumber, runScenario);

            panel.Controls.Add(button, 0, row);
            panel.Controls.Add(expectedLabel, 1, row);
            panel.Controls.Add(resultLabel, 2, row);

            _errorScenarioButtons[scenarioNumber] = button;
            _errorScenarioResultLabels[scenarioNumber] = resultLabel;
        }

        /// <summary>
        /// 시나리오 하나를 백그라운드 스레드에서 실행하고(UI 스레드를 막지 않는다 — 6/7번은 수 초
        /// 걸린다) 결과를 그 행의 결과 라벨에 반영한다. 실행 중에는 그 버튼만 비활성화한다.
        /// </summary>
        private async System.Threading.Tasks.Task RunErrorScenarioAsync(int scenarioNumber, Func<string> runScenario)
        {
            var button = _errorScenarioButtons[scenarioNumber];
            var resultLabel = _errorScenarioResultLabels[scenarioNumber];

            button.Enabled = false;
            resultLabel.Text = "실행 중…";
            resultLabel.ForeColor = Color.DarkBlue;

            try
            {
                string result = await System.Threading.Tasks.Task.Run(runScenario);
                resultLabel.Text = result;
                // 모든 시나리오 메서드는 문제 있는 경로(기대와 다름/불일치/오류)에서 예외 없이
                // "확인 필요"라는 문구를 반드시 포함하도록 통일돼 있다(ErrorInjectionClient.cs
                // 전수 확인). 예전에는 "불일치"만 찾아 시나리오 3의 타임아웃 분기("기대와 다름 …
                // 확인 필요"에는 "불일치"가 없다)가 초록(정상)으로 잘못 표시됐다(2026-08-31 검증에서
                // 발견 — 낮음, 검증 도구의 오탐).
                resultLabel.ForeColor = result.Contains("확인 필요") ? Color.DarkRed : Color.DarkGreen;
            }
            catch (Exception ex)
            {
                resultLabel.Text = $"[예외] {ex.GetType().Name}: {ex.Message}";
                resultLabel.ForeColor = Color.DarkRed;
            }
            finally
            {
                button.Enabled = true;
            }
        }
    }
}
