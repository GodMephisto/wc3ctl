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
