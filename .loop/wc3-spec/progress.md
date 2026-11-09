# progress

Append only. One block per batch, with the revision it produced.

## B1, SEC, revision 1.33

- Closed SEC-01, SEC-02, SEC-03, SEC-05, SEC-07.
- Endpoint scan found exactly the four accesses the document lists, nothing else opens a socket.
- Pinned SHA-256 for model, vocabulary and dataset, and rag_install takes only threads and batch, so no caller can point the corpus somewhere.
- Resource read refused a traversal, an encoded traversal and a file URI.
- Added one sentence to 6.11 so SEC-05 is true rather than implied.
- Gaps, G-84 the graph store reads NEO4J_PASSWORD, G-85 no hostile input has been fed to the parsers.

## B2, TST, revision 1.34

- Closed TST-01, TST-05, TST-06, TST-07, TST-10, TST-13.
- Protocol suite covers all seven surfaces, parity both directions, references byte for byte, setup over temp paths, hermetic categories, CI on windows-latest for every pull request.
- Gap, G-86 no family has a result size test.
- Concurrency, another session edited the same tree during this batch. The description total and the catalog size moved to 84,550 and 127,478 under me, two gap requirements closed, and one historical changelog row was rewritten by a blind replace and had to be restored to 84,319. The revision is therefore not solely this batch's work.

## B3, RES, revision 1.35

- Closed RES-05, RES-07, RES-09. A render inlines only under a million bytes and always reports the path, enums travel as names, and an allocated rawcode is the same against two copies of one map.
- Gaps, RES-01 quickstart returns a bare array where everything else returns an object, RES-06 the CLI emits PascalCase where the tool emits camelCase for the same record, RES-08 nothing pins a field name.
- RES-06 is the finding, it is the same record with two spellings and it is exactly what the requirement forbids.

## B5, IDN, revision 1.36

- Closed IDN-01 and IDN-04. Identity comes from assembly metadata, and the registered name is the one setup uses to remove.
- Gaps, IDN-03 version_info lacks the product id, the protocol revision and the SDK version, IDN-05 the nogit fallback has no test, IDN-06 the revision is readable only from the handshake.
- Ran out of the batch before CMP, PRF, SUR, DSC, ENV and SAF, 23 rows left in the queue.

## B4, CMP, revision 1.37

- Closed CMP-02 and CMP-06. The SDK is pinned at 1.4.1 where the protocol tests can hold it, and the server offers tools, resources and prompts and requires none of the four optional capabilities.
- Gaps, CMP-03 no alias window, CMP-04 nothing keeps fields additive, CMP-05 the degraded result is not stated.

## B9, ENV, revision 1.37

- Closed ENV-06, the server runs with no settings file and no game install.
- Gaps, ENV-01 and ENV-03. The scan is the finding, nineteen variables are read and five are documented, `HF_TOKEN` and the seven `WC3CTL_*` switches among the undocumented ones.

## B10, SAF, revision 1.37

- SAF-08 and SAF-09 closed, both output defaults are stated and one class holds the extract rule for all three front ends.
- SAF-12 is a gap. Nothing refuses a symbolic link anywhere in the tree.

## B6, B7, B8, PRF, SUR, DSC, revision 1.38

- Seven closed, one loader, one extract rule, the orientation, the 42 ms list, the retrieval fallback.
- Seven gaps. Two measurements contradicted the document, 11 parameters against a stated 8, and no test comparing the four surface views.
- The tooling had a ceiling at 99 rows. Three scripts matched two digits, so six new rows were invisible to the counts guard and missing from requirements.json. Fixed to two or three digits.
- Unassessed is 0. 90 met, 89 gap, 86 rows, spec 1.38, 67 pages dark and 77 light.

## Round two, three workers, revision pending

- First team closed seven rows at 1.45, the write gate family narrowing from 57 writers to two and the five resource requirements. Verified here, 67 tests green, 13 measures agreeing.
- Second team dispatched on disjoint files. A owns Wc3Tools.cs for SUR-07, PAR-08, DSC-05 and ERR-05. B owns the host for OBS-02, OBS-04, TRN-03 and PRF-05. C owns the scripts and CI for IDN-02, PKG-13, DOC-04 and AUT-02.
- Nobody builds but the parent, because three dotnet builds on one project collide in obj and the output assembly.
- Known defect handed to C, the version script reads the other session's binary, wc3-mcp.exe, rather than Wc3.Mcp.exe which is the one the spec measures.
