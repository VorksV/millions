using System;

using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.ViewModels 
{
 public class NotificationViewModel
 {
 public string Title {
 get;

 set;

 }
 = string.Empty;

 public string Message {
 get;

 set;

 }
 = string.Empty;

 public DateTime Timestamp {
 get;

 set;

 }
 public NotificationType Type {
 get;

 set;

 }
 public string TimeGroup {
 get {
 var diff = DateTime.Now - Timestamp;

 if (diff.TotalMinutes < 60) return "Agora";

 if (Timestamp.Date == DateTime.Today) return "Hoje";

 if (Timestamp.Date == DateTime.Today.AddDays(-1)) return "Ontem";

 return "Anteriores";

 }
 }
 }
 }
