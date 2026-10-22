// Decompiles every function of the current program into one C file (arg 0 = output path).
// Run headless: analyzeHeadless <proj dir> <proj name> -import EXE -postScript ExportDecompiled.java OUT.c
// @category BreezySRPG95
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;

import java.io.PrintWriter;

public class ExportDecompiled extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String out = getScriptArgs().length > 0 ? getScriptArgs()[0] : "decompiled.c";
        DecompInterface ifc = new DecompInterface();
        ifc.openProgram(currentProgram);
        int n = 0;
        try (PrintWriter w = new PrintWriter(out, "UTF-8")) {
            for (Function f : currentProgram.getFunctionManager().getFunctions(true)) {
                if (monitor.isCancelled()) break;
                DecompileResults r = ifc.decompileFunction(f, 60, monitor);
                w.println("// ===== " + f.getName() + " @ " + f.getEntryPoint() + " =====");
                if (r != null && r.decompileCompleted()) w.println(r.getDecompiledFunction().getC());
                else w.println("// (decompilation failed)");
                n++;
            }
        }
        println("decompiled " + n + " functions to " + out);
    }
}
