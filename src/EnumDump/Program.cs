using System;
using HIDMaestro;
// HMHat enum 값
Console.WriteLine("=== HMHat enum ===");
foreach (var name in Enum.GetNames(typeof(HMHat)))
    Console.WriteLine($"  {name} = {(byte)(HMHat)Enum.Parse(typeof(HMHat), name)}");
// HMController 타입 확인
Console.WriteLine("=== HMController.SubmitState ===");
var methods = typeof(HMController).GetMethods();
foreach (var m in methods) if (m.Name == "SubmitState") Console.WriteLine($"  {m}");
