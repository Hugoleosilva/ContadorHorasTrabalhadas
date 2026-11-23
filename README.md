# Relógio Acompanhamento

Aplicativo desktop ultraleve para Windows que acompanha as horas efetivamente trabalhadas no dia.

## Recursos

- **Contagem real de horas trabalhadas**: congela automaticamente ao bloquear a tela (`Win + L` ou bloqueio automático do Windows), deslogar, suspender/hibernar ou desligar, e retoma ao voltar.
- **Pausa manual e início opcional**:
  - Botão `⏸ / ▶` direto no widget (e no menu da bandeja) para saídas rápidas sem bloquear o PC. Não pausa por mouse parado (ideal para reuniões, palestras e treinamentos).
  - No menu de clique direito, você pode marcar/desmarcar **Iniciar Contagem ao Abrir** (se desmarcar, o app abre pausado esperando você clicar em `▶`).
  - Também há a opção **Zerar Contagem de Hoje...** no menu de clique direito caso precise limpar testes do dia atual.
- **Mini-widget ajustável**: inicia no canto inferior direito, sempre visível (configurável), arrastável, redimensionável pelas bordas ou via presets (*Compacto*, *Padrão*, *Grande*).
- **Minimizar / Visualizar na Barra de Tarefas**:
  - Por padrão, o ícone fica **fixo na Barra de Tarefas** mesmo quando você minimiza (`—`), permitindo clicar no ícone para abrir/minimizar novamente em 1 clique.
  - Passar o mouse sobre o botão na barra de tarefas (ou no ícone da bandeja) mostra o tempo trabalhado ao vivo mesmo minimizado.
  - Atalho global **`Ctrl + Alt + R`** também alterna a janela a qualquer momento.
  - Se preferir ocultar até da barra de tarefas (ficando só na bandeja ao lado do relógio), desmarque **Manter na Barra de Tarefas** no menu de clique direito.
- **Alertas de jornada CLT (8h + 2h extras)**:
  - `07:45` — pré-aviso (15 min para 8h)
  - `08:00` — jornada diária concluída / início de hora extra
  - `09:45` — pré-aviso do limite legal (15 min para 10h)
  - `10:00` — limite legal de 10h atingido (destaque visual + notificação + alerta sonoro)
- **Registros em CSV simples** (salvos em `%LOCALAPPDATA%\RelogioAcompanhamento\`):
  - `registros_diarios.csv` — resumo de cada dia (abre direto no Excel)
  - `sessoes_YYYY-MM.csv` — detalhamento de todos os intervalos do mês
  - `config.ini` — tamanho, posição e preferências

## Tecnologias utilizadas e o porquê

Este projeto foi construído priorizando **leveza máxima, simplicidade e zero dependências externas**.

| Camada | Tecnologia | Por que foi escolhida |
| :--- | :--- | :--- |
| **Linguagem** | **C#** (compilado via `csc.exe` nativo do Windows) | Linguagem fortemente tipada, rápida e com integração direta ao ecossistema Windows. |
| **Plataforma / Runtime** | **.NET Framework 4.8** (já embutido no Windows 10/11) | Sim, o projeto usa **.NET** — especificamente o .NET Framework 4.8 que já vem instalado nativamente no Windows. Isso evita baixar SDKs ou runtimes extras e gera um único executável de **~62 KB** que consome apenas **~10–15 MB de RAM**. |
| **Interface Gráfica (UI)** | **Windows Forms (WinForms) + GDI+** (`System.Windows.Forms`, `System.Drawing`) | Muito mais leve que frameworks baseados em navegador (como Electron). O desenho customizado em GDI+ permite redimensionar o mini-widget livremente com escala proporcional das fontes e sem cortes em telas com zoom (DPI). |
| **Integração com o Windows** | **SystemEvents** (`Microsoft.Win32`) + **Win32 API (P/Invoke)** | `SystemEvents` captura com precisão eventos reais do sistema operacional (bloqueio `Win + L`, bloqueio automático, logoff, suspensão e desligamento). Win32 (`user32.dll` / `dwmapi.dll`) fornece atalho global (`Ctrl + Alt + R`), cantos arredondados no Windows 11, arraste/redimensionamento sem bordas e instância única. |
| **Armazenamento** | **Arquivos CSV + INI** (UTF-8 com BOM, separador `;`) | Formato mais simples e leve possível: dispensa banco de dados externo (como SQLite), não exige DLLs adicionais e pode ser aberto diretamente no Excel ou Bloco de Notas. |
| **Build e Testes** | **PowerShell** (`build.ps1`, `run-tests.ps1`) + testes unitários em C# puro | Compila o projeto e gera o ícone `.ico` em menos de 1 segundo usando apenas ferramentas nativas do Windows, além de rodar a suíte de testes sem frameworks externos. |

### C# e .NET são coisas diferentes?

Sim — eles trabalham juntos:
- **C#** é a **linguagem de programação** usada para escrever o código-fonte (arquivos `.cs` em `src/`).
- **.NET** (neste projeto, o **.NET Framework 4.8**) é a **plataforma/runtime** que fornece o compilador (`csc.exe`), as bibliotecas padrão do Windows (janelas, gráficos, eventos de sessão, registro) e o ambiente que executa o `RelogioAcompanhamento.exe`.

> **Por que não usar Electron, Python ou .NET 8?**
> - **Electron** empacota um navegador Chromium inteiro (~100–150 MB de RAM e executável de dezenas de MB) — pesado demais para um relógio que fica aberto o dia todo.
> - **Python** exigiria instalar o interpretador ou empacotar um `.exe` grande (~50–80 MB) e depender de bibliotecas extras (`pywin32`) para eventos de bloqueio do Windows.
> - **.NET 8** exigiria baixar e instalar o SDK separadamente (ou embutir ~60 MB de runtime no `.exe`). Com o **.NET Framework 4.8** nativo do Windows, o resultado final tem **62 KB** e funciona imediatamente.

## Como compilar e testar

```powershell
# Compilar o executável único em dist\RelogioAcompanhamento.exe
.\build.ps1

# Rodar os testes unitários
.\run-tests.ps1

# Testar alertas em modo rápido (1 min normal + 30s extra, sem alterar seus dados reais)
.\dist\RelogioAcompanhamento.exe --demo-limits
```
