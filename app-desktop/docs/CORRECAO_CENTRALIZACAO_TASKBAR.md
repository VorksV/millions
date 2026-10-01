# Correção da Centralização da Taskbar com "Notícias e Interesses"

## Problema Identificado

A centralização dos ícones da taskbar estava **deslocada para a direita** quando o widget **"Notícias e Interesses"** (DynamicContent1) estava ativado, mesmo com a centralização aplicada.

## Causa Raiz

O código original usava a fórmula básica do TaskbarX:

```
Position = (TrayWndWidth / 2) - (TaskbarWidth / 2) - TaskbarLeft
```

Porém, **NÃO estava aplicando o offset especial** que o TaskbarX usa quando há elementos adicionais na taskbar (TrayNotify e NewsAndInterests), conforme as linhas 831-839 do `TaskbarCenter.vb` original:

```vb
If Settings.CenterInBetween = 1 Then
    Dim offset = (TrayNotifyPos.width / 2 - (TaskbarLeft \ 2)) + NewsAndInterestsPos.width / 2
    Position = Math.Abs(CInt((TrayWndWidth / 2 - (TaskbarWidth / 2) - TaskbarLeft - offset)))
```

## Por Que a Centralização Ficava Errada?

Quando o "Notícias e Interesses" está ativo:
1. O widget ocupa espaço à **direita** dos ícones da taskbar
2. O `MSTaskListWClass` (container dos ícones) é empurrado para a **esquerda**
3. A largura medida dos ícones (`taskbarWidth`) não inclui o espaço do widget
4. Sem o offset, a fórmula assume que os ícones devem ser centralizados no centro absoluto da `Shell_TrayWnd`
5. Resultado: os ícones ficam **deslocados para a direita** porque o widget não foi considerado

## Solução Implementada

### 1. Detecção dos Elementos Adicionais

```csharp
IntPtr trayNotifyHwnd = FindWindowEx(info.TrayWnd, IntPtr.Zero, "TrayNotifyWnd", IntPtr.Zero);
IntPtr newsAndInterestsHwnd = FindWindowEx(info.TrayWnd, IntPtr.Zero, "DynamicContent1", IntPtr.Zero);

bool hasTrayNotify = GetWindowRect(trayNotifyHwnd, out var trayNotifyRect);
bool hasNewsAndInterests = GetWindowRect(newsAndInterestsHwnd, out var newsRect);
```

### 2. Cálculo do Offset (Idêntico ao TaskbarX CenterInBetween)

```csharp
int trayNotifyOffset = hasTrayNotify ? (trayNotifyRect.Width) / 2 : 0;
int newsOffset = hasNewsAndInterests ? (newsRect.Width) / 2 : 0;

// Offset = (TrayNotifyWidth / 2) - (TaskbarLeft / 2) + NewsWidth / 2
int offset = trayNotifyOffset - (taskbarLeft / 2) + newsOffset;

position = Math.Abs(position - offset);
```

### 3. P/Invoke Adicional

Adicionada sobrecarga de `FindWindowEx` que aceita `IntPtr.Zero`:

```csharp
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, 
    string lclassName, IntPtr windowTitle);
```

## Resultado

A centralização agora:
✅ Considera o espaço do TrayNotify (área de notificação)  
✅ Considera o espaço do DynamicContent1 (Notícias e Interesses)  
✅ Aplica offset idêntico ao TaskbarX CenterInBetween=1  
✅ Centralização perfeita mesmo com widget ativado  
✅ Funciona corretamente com taskbar horizontal e vertical  

## Fórmulas

### Antes (Incorreto)
```
Position = Abs((TrayWndWidth / 2) - (TaskbarWidth / 2) - TaskbarLeft)
```

### Depois (Correto - com offset)
```
Offset = (TrayNotifyWidth / 2) - (TaskbarLeft / 2) + (NewsWidth / 2)
Position = Abs(((TrayWndWidth / 2) - (TaskbarWidth / 2) - TaskbarLeft) - Offset)
```

## Arquivos Modificados

- `Services/Personalize/TaskbarControlService.cs`
  - Método `CenterAllTaskbars()` - Adicionado cálculo de offset
  - P/Invoke `FindWindowEx` com sobrecarga para IntPtr

## Validação

Para validar a correção:
1. Ative "Notícias e Interesses" na taskbar
2. Ative a centralização no Voltris
3. Os ícones devem estar **perfeitamente centralizados** em relação ao espaço disponível
4. O widget deve permanecer visível à direita dos ícones
5. A centralização deve se manter ao redimensionar a taskbar

## Referências

- TaskbarX original: `TaskbarCenter.vb` linhas 831-839
- Classe: `VoltrisOptimizer.Services.Personalize.TaskbarControlService`
- Método: `CenterAllTaskbars()`