namespace SKONanobotBuildAndRepairSystem
{
    public static class CollectManager
    {
        public static void TryCollectingFloatingTargets(
            NanobotBuildAndRepairSystemBlock block,
            out bool collecting,
            out bool needCollecting,
            out bool transporting)
        {
            collecting = false;
            needCollecting = false;
            transporting = false;

            if (!PowerManager.HasRequiredElectricPower(block))
            {
                return;
            }

            lock (block.State.PossibleFloatingTargets)
            {
                Logging.Instance?.Write(Logging.Level.Info,
                    "BuildAndRepairSystemBlock {0}: ServerTryCollectingFloatingTargets PossibleFloatingTargets={1}",
                    Logging.BlockName(block.Welder, Logging.BlockNameOptions.None),
                    block.State.PossibleFloatingTargets.CurrentCount);

                TargetEntityData collectingFirstTarget = null;
                var collectingCount = 0;

                foreach (var targetData in block.State.PossibleFloatingTargets)
                {
                    if (targetData.Entity != null && !targetData.Ignore)
                    {
                        Logging.Instance?.Write(Logging.Level.Verbose,
                            "BuildAndRepairSystemBlock {0}: ServerTryCollectingFloatingTargets: {1} distance={2}",
                            Logging.BlockName(block.Welder, Logging.BlockNameOptions.None),
                            Logging.BlockName(targetData.Entity),
                            targetData.Distance);                     

                        needCollecting = true;

                        var added = block.ServerDoCollectFloating(targetData, out transporting, ref collectingFirstTarget);

                        if (targetData.Ignore)
                        {
                            block.State.PossibleFloatingTargets.ChangeHash();
                        }

                        collecting |= added;
                        if (added) collectingCount++;

                        if (transporting || collectingCount >= Constants.COLLECT_FLOATINGOBJECTS_SIMULTANEOUSLY)
                        {
                            break;
                        }
                    }
                }

                if (collecting && !transporting)
                {
                    block.ServerDoCollectFloating(null, out transporting, ref collectingFirstTarget);
                }
            }
        }
    }
}