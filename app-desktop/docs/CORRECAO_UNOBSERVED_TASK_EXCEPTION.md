# Correção de UnobservedTaskException

## Problema Identificado

O erro `[UNOBSERVED TASK EXCEPTION]` ocorria porque várias tasks assíncronas estavam sendo executadas em modo "fire-and-forget" sem tratamento adequado de exceções. Quando uma task falha e sua exceção não é observada (via `await`, `.Wait()`, ou acesso à propriedade `.Exception`), o .NET relança essa exceção na finalizer thread.

## Causa Raiz

```csharp
// ❌ CÓDIGO PROBLEMÁTICO (antes)
_ = Task.Run(async () => {
    await AlgumaOperacaoAsync(); // Se falhar, exceção não é observada
});
```

## Solução Implementada

### 1. Helpers de Task Segura

Foram adicionados métodos em `AsyncHelper.cs`:

```csharp
// ✅ CÓDIGO CORRIGIDO (depois)
_ = AsyncHelper.SafeTaskRun(async () => {
    await AlgumaOperacaoAsync(); // Exceções são capturadas e logadas
});
```

### 2. Métodos Disponíveis

- `SafeTaskRun(Func<Task>)` - Para tasks sem retorno
- `SafeTaskRun<T>(Func<Task<T>>)` - Para tasks com retorno
- `FireAndForgetSafe(this Task, Action<Exception>)` - Extension method para tasks existentes
- `ExceptionHelper.SafeExecuteAsync` - Para execução com contexto de logging

### 3. Handler Global Reforçado

O handler em `App.xaml.cs` foi melhorado para:
- Extrair a mensagem da exception interna correta
- Garantir que todas as exceptions sejam marcadas como observadas
- Prevenir crash da finalizer thread

## Boas Práticas

1. **Sempre use `await`** quando possível
2. **Use `SafeTaskRun`** para operações fire-and-forget
3. **Nunca ignore tasks** com `_ = Task.Run()` sem tratamento
4. **Capture CancellationToken** para cancellation handling adequado
5. **Use `ConfigureAwait(false)`** em código que não precisa da UI thread

## Arquivos Modificados

- `Helpers/AsyncHelper.cs` - Novos métodos SafeTaskRun
- `Helpers/ExceptionHelper.cs` - Novos métodos SafeExecuteAsync
- `App.xaml.cs` - Handler global e tasks corrigidas

## Validação

Após esta correção, o erro não deve mais ocorrer. Caso ocorra, verifique:
1. Se há novas tasks sendo criadas sem `SafeTaskRun`
2. Se alguma operação de I/O está sendo cancelada sem tratamento
3. Logs em `[UNOBSERVED TASK EXCEPTION]` para identificar a origem