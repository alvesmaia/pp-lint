# Documentação das regras

Um arquivo por regra do catálogo. São os mesmos documentos embarcados no binário — o que
`pp-lint explain <ID>` imprime, e o que os `helpUri` dos relatórios SARIF referenciam.

Ao alterar uma regra, altere `src/PpLint.Rules/Docs/<ID>.md` e copie para cá. O teste
`EveryEmbeddedDocumentIsAlsoPublishedInTheRepository` falha se um documento existir só de
um lado.
