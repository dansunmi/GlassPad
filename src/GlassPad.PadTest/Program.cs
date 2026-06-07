// GlassPad Step 1 — HIDMaestro virtual Xbox 360 pad smoke test
// 관리자 권한 필요 (드라이버 설치)
// 검증: joy.cpl 열어서 "Xbox 360 Controller" 보이면 성공

using HIDMaestro;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("Windows 전용입니다.");
    return 1;
}

Console.WriteLine("=== GlassPad PadTest — HIDMaestro 환경 검증 ===\n");

// 이전 실행에서 크래시로 남은 OEM 이름 오버라이드 정리
int recovered = HMOemNameOverride.RecoverOrphans();
if (recovered > 0)
    Console.WriteLine($"[복구] 이전 세션 OEM 오버라이드 {recovered}개 정리");

// 1. SDK 초기화 + 프로파일 로드
Console.Write("프로파일 로드 중...");
using var ctx = new HMContext();
int loaded = ctx.LoadDefaultProfiles();
Console.WriteLine($" {loaded}개 로드 완료");

// 2. 드라이버 설치 (최초 1회 ~18초, 이후 ~즉시)
Console.Write("드라이버 설치 중 (최초 실행 시 ~18초 소요)...");
ctx.InstallDriver();
Console.WriteLine(" 완료");

// 3. Xbox 360 가상 패드 생성
var profile = ctx.GetProfile("xbox-360-wired")
    ?? throw new InvalidOperationException("xbox-360-wired 프로파일을 찾을 수 없습니다.");

Console.Write($"가상 패드 생성 중 ({profile.Name})...");
using var ctrl = ctx.CreateController(profile);
Console.WriteLine(" 완료");
Console.WriteLine();
Console.WriteLine(">>> 지금 joy.cpl (Windows 키 → '게임 컨트롤러 설정')을 열어서");
Console.WriteLine("    'Xbox 360 Controller' 장치가 나타나는지 확인하세요. <<<");
Console.WriteLine();

// 4. A버튼 5회 ON/OFF
for (int i = 1; i <= 5; i++)
{
    Console.WriteLine($"[{i}/5] A 버튼 ON");
    ctrl.SubmitState(new HMGamepadState { Buttons = HMButton.A });
    Thread.Sleep(1000);

    Console.WriteLine($"[{i}/5] A 버튼 OFF");
    ctrl.SubmitState(new HMGamepadState { });
    Thread.Sleep(500);
}

Console.WriteLine();
Console.WriteLine("=== 테스트 완료. 가상 패드 제거 중... ===");
// using 블록 종료 시 ctrl.Dispose() 자동 호출

return 0;
