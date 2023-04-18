package com.tpbeldie;
import java.util.UUID;

import com.bitwig.extension.api.PlatformType;
import com.bitwig.extension.controller.AutoDetectionMidiPortNamesList;
import com.bitwig.extension.controller.ControllerExtensionDefinition;
import com.bitwig.extension.controller.api.ControllerHost;

public class fsptransmitterExtensionDefinition extends ControllerExtensionDefinition
{
   private static final UUID DRIVER_ID = UUID.fromString("71f3ef1c-677d-4b05-bcec-8bc029ef457a");
   
   public fsptransmitterExtensionDefinition()
   {
   }

   @Override
   public String getName()
   {
      return "FSP-Transmitter";
   }
   
   @Override
   public String getAuthor()
   {
      return "tpbeldie";
   }

   @Override
   public String getVersion()
   {
      return "1";
   }

   @Override
   public UUID getId()
   {
      return DRIVER_ID;
   }
   
   @Override
   public String getHardwareVendor()
   {
      return "github.com/tpbeldie";
   }
   
   @Override
   public String getHardwareModel()
   {
      return "FSP-Transmitter";
   }

   @Override
   public int getRequiredAPIVersion()
   {
      return 17;
   }

   @Override
   public int getNumMidiInPorts()
   {
      return 0;
   }

   @Override
   public int getNumMidiOutPorts()
   {
      return 0;
   }

   @Override
   public void listAutoDetectionMidiPortNames(final AutoDetectionMidiPortNamesList list, final PlatformType platformType) {
   }

   @Override
   public fsptransmitterExtension createInstance(final ControllerHost host)
   {
      return new fsptransmitterExtension(this, host);
   }
}
