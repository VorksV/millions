// VOLTRIS - usings globais
//
// LogDirectoryResolver passou a ser usado em ~50 call sites que criavam o
// diretorio de logs com AppDomain.CurrentDomain.BaseDirectory, espalhados por
// ~40 arquivos. Declarar o using aqui evita adicionar 50 linhas de using e
// mantem as chamadas curtas e legiveis.
global using VoltrisOptimizer.Services.Logging;
