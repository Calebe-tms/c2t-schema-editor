# DBML Editor

Editor e visualizador de diagramas ER para arquivos **DBML**, estilo dbdiagram.io, que roda localmente no Windows.
Dê um duplo clique num `.dbml` e ele abre no Chrome (ou Edge): código à esquerda, diagrama à direita.

- Arraste as tabelas para organizar o diagrama (as posições ficam salvas por arquivo).
- Clique numa tabela e o código rola até ela; mova o cursor no código e a tabela é destacada.
- O diagrama atualiza enquanto você digita; erros de sintaxe aparecem com a linha.
- `Ctrl+S` grava direto no `.dbml`. Se o arquivo mudar em disco, o editor recarrega.
- Sem dependências: parser DBML próprio, tudo offline, nenhum npm.
- **Também abre o `schema.ts` do Drizzle** (`.ts`): mesmo editor e diagrama, mas o código é o TypeScript e o `Ctrl+S` grava no próprio `schema.ts`. O diagrama é só uma leitura do código; para mudar a estrutura, edite o texto. Veja "Schema do Drizzle" abaixo.

## Instalação (Windows 10/11)

**Só instalar:** dê duplo clique em `dist\DBML-Editor-Setup.exe` (já vem pronto no repositório, nada para compilar).

Para regenerar o instalador depois de mudar o código (só precisa do Windows, nada mais):

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

Isso cria `dist\DBML-Editor-Setup.exe` (~80 KB), um instalador único que pode ser distribuído para qualquer máquina com Windows 10/11.
Ao executá-lo, ele instala em `%LOCALAPPDATA%\Programs\DBML Editor` (sem admin), cria atalho no Menu Iniciar, associa `.dbml`
e registra o programa em *Configurações › Aplicativos instalados* (desinstalação por lá, ou `Uninstall.exe`).
No primeiro duplo clique num `.dbml`, escolha **DBML Editor** e **Sempre**. Abrir pelo Menu Iniciar mostra um seletor de arquivo.
`DBML-Editor-Setup.exe /S` instala em silêncio.

## Como funciona

`DbmlEditor.exe` sobe um servidor HTTP local (só `127.0.0.1`, porta aleatória, token na URL) que serve a página embutida no próprio exe
e grava o arquivo; abre o Chrome (ou Edge) em janela limpa e encerra sozinho ao fechá-la. Layout e preferências ficam em `%APPDATA%\dbml-viewer`.

## Suporte DBML

`Table` (alias, `headercolor`, notas, `indexes`), `Enum`, `Ref` (`>`, `<`, `-`, `<>`, inline, em bloco, compostas), schemas e nomes entre aspas.
`Project` e `TableGroup` são lidos mas não desenhados; `TablePartial` é ignorado.

## Schema do Drizzle (`.ts`)

Como abrir: botão direito no `schema.ts` › **Abrir no DBML Editor** (primeiro nível do menu, só em arquivos com esse nome), pelo Menu Iniciar (o seletor aceita `.dbml` e `.ts`), por **Abrir com**, ou pela linha de comando: `DbmlEditor.exe caminho\schema.ts`.
O instalador **não** associa `.ts` ao app (isso sequestraria todos os arquivos TypeScript): o duplo clique continua no editor de sempre.

O modo é escolhido pela extensão do arquivo. No modo `.ts`:

- O código à esquerda é o `schema.ts` com realce de TypeScript (indenta com tab, como o arquivo). O diagrama é redesenhado enquanto você digita.
- Lê `pgSchema`, `schema.table(...)`/`pgTable(...)`, `schema.enum(...)`/`pgEnum(...)`, tipos e modificadores das colunas (`notNull`, `primaryKey`, `default`, `unique`, `array`, `references`) e, no 3º argumento da tabela, `index`, `uniqueIndex`, `unique`, `primaryKey`, `foreignKey` e `check`.
- Relações vêm de `foreignKey({...})` e de `.references(() => tabela.coluna)`; colunas com enum ligam-se ao enum no diagrama.
- Índices e checks aparecem na dica (passar o mouse) do cabeçalho de cada tabela.
- Não é um parser completo de TypeScript. Construções fora do padrão do `drizzle-kit pull` são ignoradas; avisos aparecem na barra de status e erros de sintaxe (aspas, chaves) mostram a linha.
- O `Ctrl+S` grava o texto direto no arquivo. Depois de salvar é preciso gerar a migration (`drizzle-kit generate`) como de costume: o editor não toca no banco.
- **Pendências de migration:** o servidor lê o snapshot mais recente em `meta/NNNN_snapshot.json` (pasta `meta` ao lado do `schema.ts`) e o diagrama marca o que o `schema.ts` tem de diferente: tabela/enum **nova** (borda verde), **alterada** (borda âmbar) ou **removida** do `schema.ts` (cartão fantasma de borda vermelha tracejada), sempre com um selo no cabeçalho; colunas novas/alteradas (tipo, null ou PK) e removidas ganham uma barra lateral na mesma cor. A comparação é só com a última migration **gerada**: não diz se ela já foi aplicada no banco. Ao voltar para a janela o snapshot é relido (útil depois do `drizzle-kit generate`).
- Cursor ou clique no código dentro de uma tabela marca a tabela no diagrama (como o clique nela) e a traz para a vista se estiver fora.
- O botão **Detalhes** no cabeçalho de cada tabela (e de cada enum) abre um painel à direita com o que não cabe no cartão: se a tabela tem RLS (`.enableRLS()`), chaves estrangeiras que saem e as que chegam (com `ON DELETE`/`ON UPDATE`, ou "NO ACTION (padrão)" quando não declarados), índices (únicos, parciais, de expressão) e checks. Nos enums mostra os valores e as colunas que os usam. Os nomes de tabela no painel são links para o diagrama; `Esc` fecha. Só no modo `.ts`.
- Variável ou tabela declarada duas vezes vira aviso na barra de status (com as linhas) e as duas aparecem no diagrama.
- O botão **Legenda** mostra/oculta a legenda (oculta por padrão).

### Cores: um significado por canal

| Canal | O que diferencia |
|---|---|
| **Borda** do cartão | estado de migration: neutra (igual ao snapshot), verde (nova), âmbar (alterada), vermelha tracejada (removida) |
| **Título** | tipo: claro = tabela, lilás + "enum" = enum |
| **Cabeçalho** (fundo) | schema: um tom sóbrio por schema (no DBML, `headercolor` prevalece) |
| **Anel** em volta | destaque: azul = selecionada, claro = resultado da busca |

O anel não usa a borda, então uma tabela nova continua verde mesmo selecionada. PK e FK são selos neutros, sem cor própria.

## Estrutura

| Arquivo | Função |
|---|---|
| `dbml-viewer/viewer.html` | página e estilos |
| `dbml-viewer/js/parser.js` | tokenizer e parser DBML |
| `dbml-viewer/js/drizzle/ts-parser.js` | tokenizer e árvore de expressões do TypeScript (modo `.ts`) |
| `dbml-viewer/js/drizzle/drizzle-model.js` | transforma a árvore em tabelas, colunas, enums e relações do Drizzle |
| `dbml-viewer/js/drizzle/ts-highlight.js` | realce de sintaxe do TypeScript no editor |
| `dbml-viewer/js/drizzle/snapshot-diff.js` | compara o schema com o snapshot do Drizzle (nova/alterada/removida) |
| `dbml-viewer/js/layout.js` | organização automática das tabelas |
| `dbml-viewer/js/editor.js` | editor de código com realce |
| `dbml-viewer/js/app.js` | diagrama, arrastar, salvar, sincronização código ↔ diagrama |
| `installer/App.cs` | `DbmlEditor.exe`: servidor local + abre o navegador (embute a página) |
| `installer/Setup.cs` | instalador/desinstalador (arquivos, associação `.dbml`, atalho) |
| `installer/build.ps1` | compila tudo com o `csc.exe` do Windows e gera `dist\DBML-Editor-Setup.exe` |
